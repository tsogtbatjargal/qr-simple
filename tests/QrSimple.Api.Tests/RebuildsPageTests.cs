using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class InspectionsPageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anyone_can_view_the_inspections_page_without_logging_in()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(operatorClient, "Anon Inspections Pump");

        using var content = TestUploads.Inspection(note: "Routine check, all good.");
        await operatorClient.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        var anonymousClient = factory.CreateClient();
        var response = await anonymousClient.GetAsync($"/e/{equipment.Id}/inspections");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("Anon Inspections Pump", html);
        Assert.Contains("Routine check, all good.", html);
    }

    [Fact]
    public async Task Equipment_serial_number_appears_on_the_page()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Serial Visible Pump");

        var html = await client.GetStringAsync($"/e/{equipment.Id}/inspections");

        Assert.Contains(equipment.SerialNumber, html);
    }

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

        using var content = TestUploads.Inspection();
        await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        var anonymousClient = factory.CreateClient();
        var html = await anonymousClient.GetStringAsync($"/e/{equipment.Id}/inspections");

        Assert.DoesNotContain(email, html);
        Assert.DoesNotContain("@example.com", html);
    }

    [Fact]
    public async Task A_recent_inspection_is_outside_details_and_an_old_one_is_inside()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Recency Split Pump");

        // One genuinely recent record plus four old ones: the minimum-recent-of-3 rule (decision
        // 17) will promote two of the old ones into the visible set, so this needs enough old
        // records that promotion still leaves some behind <details> — a single old record would
        // get fully promoted and <details> would never render (see the failure this replaced).
        using var recent = TestUploads.Inspection(kind: InspectionKinds.Monthly, inspectionDate: BusinessTime.Today());
        await client.PostAsync($"/equipment/{equipment.Id}/inspections", recent);

        foreach (var monthsAgo in new[] { 8, 9, 10, 11 })
        {
            using var old = TestUploads.Inspection(kind: InspectionKinds.Annual, inspectionDate: BusinessTime.Today().AddMonths(-monthsAgo));
            await client.PostAsync($"/equipment/{equipment.Id}/inspections", old);
        }

        var html = await client.GetStringAsync($"/e/{equipment.Id}/inspections");

        Assert.Contains("<details", html);
        Assert.Contains("Older inspections (2)", html);

        var detailsIndex = html.IndexOf("<details", StringComparison.Ordinal);
        var recentSectionIndex = html.IndexOf(BusinessTime.Today().ToString("d MMM yyyy"), StringComparison.Ordinal);
        var oldestSectionIndex = html.IndexOf(BusinessTime.Today().AddMonths(-11).ToString("d MMM yyyy"), StringComparison.Ordinal);

        Assert.True(recentSectionIndex < detailsIndex, "the recent inspection should render before <details>");
        Assert.True(oldestSectionIndex > detailsIndex, "the oldest inspection should render inside <details>");
    }

    [Fact]
    public async Task Equipment_with_only_old_inspections_still_surfaces_the_minimum_recent()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "All Old Annual Pump");

        foreach (var monthsAgo in new[] { 12, 24, 36, 48, 60 })
        {
            using var content = TestUploads.Inspection(kind: InspectionKinds.Annual, inspectionDate: BusinessTime.Today().AddMonths(-monthsAgo));
            var response = await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);
            Assert.True(response.IsSuccessStatusCode);
        }

        var html = await client.GetStringAsync($"/e/{equipment.Id}/inspections");

        // decision 17: even though none of the 5 records are within 6 months, 3 still show as
        // recent (outside the collapsed <details>), leaving 2 older.
        Assert.Contains("Older inspections (2)", html);

        var detailsIndex = html.IndexOf("<details", StringComparison.Ordinal);
        var mostRecentIndex = html.IndexOf(BusinessTime.Today().AddMonths(-12).ToString("d MMM yyyy"), StringComparison.Ordinal);
        Assert.True(mostRecentIndex < detailsIndex);
    }

    [Fact]
    public async Task Note_is_rendered_and_html_encoded()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Note Encoding Pump");

        using var content = TestUploads.Inspection(note: "<script>alert(1)</script>");
        await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        var html = await client.GetStringAsync($"/e/{equipment.Id}/inspections");

        Assert.DoesNotContain("<script>alert(1)</script>", html);
        Assert.Contains("&lt;script&gt;", html);
    }

    [Fact]
    public async Task Empty_state_renders_when_there_are_no_inspections()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Inspections Pump");

        var html = await client.GetStringAsync($"/e/{equipment.Id}/inspections");

        Assert.Contains("No inspection records yet.", html);
    }

    [Fact]
    public async Task Retired_equipment_inspections_page_still_renders()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(operatorClient, "Retired Inspections Pump");

        using var content = TestUploads.Inspection(note: "Last inspection before retirement.");
        await operatorClient.PostAsync($"/equipment/{equipment.Id}/inspections", content);

        var adminClient = factory.CreateClientAs("Admin");
        await adminClient.PostAsync($"/equipment/{equipment.Id}/retire", content: null);

        var response = await factory.CreateClient().GetAsync($"/e/{equipment.Id}/inspections");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Last inspection before retirement.", html);
    }

    [Fact]
    public async Task Scan_page_inspection_panel_shows_the_true_count_and_links_to_the_page()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "Panel Count Pump");

        for (var i = 0; i < 2; i++)
        {
            using var content = TestUploads.Inspection(inspectionDate: BusinessTime.Today().AddDays(-i));
            await client.PostAsync($"/equipment/{equipment.Id}/inspections", content);
        }

        var html = await client.GetStringAsync($"/e/{equipment.Id}");

        Assert.Contains($"/e/{equipment.Id}/inspections", html);
        Assert.Contains("Inspection records (2)", html);
    }

    [Fact]
    public async Task Scan_page_omits_the_inspection_panel_when_there_are_no_inspections()
    {
        var client = factory.CreateClientAs("Operator");
        var equipment = await CreateEquipmentAsync(client, "No Panel Pump");

        var html = await client.GetStringAsync($"/e/{equipment.Id}");

        Assert.DoesNotContain($"/e/{equipment.Id}/inspections", html);
        Assert.DoesNotContain("Inspection records", html);
    }

    private static async Task<CreatedEquipment> CreateEquipmentAsync(HttpClient client, string name)
    {
        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name,
            category = "Pump",
            serialNumber = $"IPG-{Guid.NewGuid():N}"[..10],
            site = "North Pit",
        });
        return (await created.Content.ReadFromJsonAsync<CreatedEquipment>())!;
    }

    private sealed record CreatedEquipment(Guid Id, string SerialNumber);
}
