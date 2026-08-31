using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class RebuildsPageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anyone_can_view_the_rebuild_history_page_without_logging_in()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(operatorClient, "Anon Rebuilds Pump");

        using var content = TestUploads.Rebuild(note: "Engine and transmission rebuilt.");
        await operatorClient.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/e/{equipment.Id}/rebuilds");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Anon Rebuilds Pump", html);
        Assert.Contains("Engine and transmission rebuilt.", html);
    }

    [Fact]
    public async Task Equipment_serial_number_appears_on_the_page()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Serial Visible Pump");

        var html = await client.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        Assert.Contains(equipment.SerialNumber, html);
    }

    // docs/plans/0002-inspection-records.md decision 11 — UploadedByEmail is stored always and
    // rendered only in the admin UI, never on the anonymously readable page.
    [Fact]
    public async Task Uploader_email_does_not_appear_anywhere_in_the_public_html()
    {
        var email = $"secret-uploader-{Guid.NewGuid():N}@example.com";

        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Users.Add(new User { Id = Guid.NewGuid(), Email = email, Role = Roles.Operator });
            await db.SaveChangesAsync();
        }

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, email);

        var equipment = await CreateEquipmentAsync(client, "Email Hidden Pump");

        using var content = TestUploads.Rebuild();
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        var anonymousClient = factory.CreateClient();
        var html = await anonymousClient.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        Assert.DoesNotContain(email, html);
        Assert.DoesNotContain("@example.com", html);
    }

    // Rebuilds are years apart, so the page renders every record flat rather than collapsing
    // anything older than six months into a <details> drawer the way the inspections page did.
    [Fact]
    public async Task Every_record_renders_outside_a_details_drawer_however_old_it_is()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Flat History Pump");

        foreach (var monthsAgo in new[] { 0, 12, 36, 60, 96 })
        {
            using var content = TestUploads.Rebuild(rebuildDate: BusinessTime.Today().AddMonths(-monthsAgo));
            var response = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
            Assert.True(response.IsSuccessStatusCode);
        }

        var html = await client.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        Assert.DoesNotContain("<details", html);
        foreach (var monthsAgo in new[] { 0, 12, 36, 60, 96 })
        {
            Assert.Contains(BusinessTime.Today().AddMonths(-monthsAgo).ToString("d MMM yyyy"), html);
        }
    }

    [Fact]
    public async Task Records_render_newest_first()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Newest First Pump");

        using var older = TestUploads.Rebuild(rebuildDate: BusinessTime.Today().AddMonths(-48));
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", older);
        using var newer = TestUploads.Rebuild(rebuildDate: BusinessTime.Today());
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", newer);

        var html = await client.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        var newestIndex = html.IndexOf(BusinessTime.Today().ToString("d MMM yyyy"), StringComparison.Ordinal);
        var oldestIndex = html.IndexOf(BusinessTime.Today().AddMonths(-48).ToString("d MMM yyyy"), StringComparison.Ordinal);
        Assert.True(newestIndex < oldestIndex, "the newest rebuild should render first");
    }

    [Fact]
    public async Task A_record_with_no_pdf_renders_without_an_open_pdf_link()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Link Pump");

        using var content = TestUploads.Rebuild(note: "Rebuilt in the field, no report.", includeFile: false);
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        var html = await client.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        Assert.Contains("Rebuilt in the field, no report.", html);
        Assert.DoesNotContain("Open PDF", html);
        Assert.DoesNotContain("/content", html);
    }

    [Fact]
    public async Task A_record_with_a_pdf_renders_a_link_to_it()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "With Link Pump");

        using var content = TestUploads.Rebuild();
        var created = await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = await created.Content.ReadFromJsonAsync<CreatedRebuild>();

        var html = await client.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        Assert.Contains("Open PDF", html);
        Assert.Contains($"/rebuilds/{rebuild!.Id}/content", html);
    }

    [Fact]
    public async Task Note_is_rendered_and_html_encoded()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Note Encoding Pump");

        using var content = TestUploads.Rebuild(note: "<script>alert(1)</script>");
        await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        var html = await client.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Empty_state_renders_when_there_are_no_rebuilds()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Rebuilds Pump");

        var html = await client.GetStringAsync($"/e/{equipment.Id}/rebuilds");

        Assert.Contains("No rebuild records yet.", html);
    }

    [Fact]
    public async Task Retired_equipment_rebuild_history_page_still_renders()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(operatorClient, "Retired Rebuilds Pump");

        using var content = TestUploads.Rebuild(note: "Last rebuild before retirement.");
        await operatorClient.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);

        var adminClient = factory.CreateClientAs("Admin");
        await adminClient.PostAsync($"/equipment/{equipment.Id}/retire", content: null);

        var response = await factory.CreateClient().GetAsync($"/e/{equipment.Id}/rebuilds");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Last rebuild before retirement.", html);
    }

    [Fact]
    public async Task Scan_page_rebuild_panel_shows_the_true_count_and_links_to_the_page()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Panel Count Pump");

        for (var i = 0; i < 2; i++)
        {
            using var content = TestUploads.Rebuild(rebuildDate: BusinessTime.Today().AddDays(-i));
            await client.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        }

        var html = await client.GetStringAsync($"/e/{equipment.Id}");

        Assert.Contains($"/e/{equipment.Id}/rebuilds", html);
        Assert.Contains("Rebuild history (2)", html);
    }

    [Fact]
    public async Task Scan_page_omits_the_rebuild_panel_when_there_are_no_rebuilds()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Panel Pump");

        var html = await client.GetStringAsync($"/e/{equipment.Id}");

        Assert.DoesNotContain($"/e/{equipment.Id}/rebuilds", html);
        Assert.DoesNotContain("Rebuild history", html);
    }

    private static async Task<CreatedEquipment> CreateEquipmentAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name,
            category = "Pump",
            serialNumber = $"RPG-{Guid.NewGuid():N}"[..10],
            site = "North Pit",
        });
        return (await created.Content.ReadFromJsonAsync<CreatedEquipment>())!;
    }

    private sealed record CreatedEquipment(Guid Id, string SerialNumber);
    private sealed record CreatedRebuild(Guid Id);
}
