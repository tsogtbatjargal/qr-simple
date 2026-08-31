using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

// Covers docs/plans/0002-inspection-records.md decisions 12-14 — the ones most likely to
// regress silently: Operator hard-delete, cross-Operator edit, and Reader write access. These
// rules carried over unchanged when inspections became rebuild history.
public class RebuildPermissionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Operator_cannot_delete_a_rebuild_record_but_admin_can()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (equipmentId, rebuildId) = await SeedRebuildAsync(operatorClient);

        var deniedResponse = await operatorClient.DeleteAsync($"/equipment/{equipmentId}/rebuilds/{rebuildId}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var adminClient = factory.CreateClientAs("Admin");
        var deleteResponse = await adminClient.DeleteAsync($"/equipment/{equipmentId}/rebuilds/{rebuildId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Operator_can_edit_a_record_they_uploaded()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (_, rebuildId) = await SeedRebuildAsync(operatorClient);

        var response = await operatorClient.PutAsJsonAsync($"/rebuilds/{rebuildId}", new
        {
            rebuildDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Corrected note.",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Operator_cannot_edit_another_operators_record()
    {
        var uploaderClient = factory.CreateClientAs("Operator");
        var (_, rebuildId) = await SeedRebuildAsync(uploaderClient);

        var otherOperatorClient = factory.CreateClientAs("Operator");
        var response = await otherOperatorClient.PutAsJsonAsync($"/rebuilds/{rebuildId}", new
        {
            rebuildDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Should not be allowed.",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_edit_any_record()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (_, rebuildId) = await SeedRebuildAsync(operatorClient);

        var adminClient = factory.CreateClientAs("Admin");
        var response = await adminClient.PutAsJsonAsync($"/rebuilds/{rebuildId}", new
        {
            rebuildDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Admin correction.",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rebuild = await db.Rebuilds.FindAsync(rebuildId);
        Assert.NotNull(rebuild!.LastEditedAtUtc);
    }

    [Fact]
    public async Task Reader_cannot_upload_edit_or_delete_a_rebuild_record()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (equipmentId, rebuildId) = await SeedRebuildAsync(operatorClient);

        var readerClient = factory.CreateClientAs("Reader");

        using var uploadContent = TestUploads.Rebuild();
        var uploadResponse = await readerClient.PostAsync($"/equipment/{equipmentId}/rebuilds", uploadContent);
        Assert.Equal(HttpStatusCode.Forbidden, uploadResponse.StatusCode);

        var editResponse = await readerClient.PutAsJsonAsync($"/rebuilds/{rebuildId}", new
        {
            rebuildDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Reader should not edit.",
        });
        Assert.Equal(HttpStatusCode.Forbidden, editResponse.StatusCode);

        using var attachContent = TestUploads.OemReport();
        var attachResponse = await readerClient.PostAsync($"/rebuilds/{rebuildId}/file", attachContent);
        Assert.Equal(HttpStatusCode.Forbidden, attachResponse.StatusCode);

        var deleteResponse = await readerClient.DeleteAsync($"/equipment/{equipmentId}/rebuilds/{rebuildId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected_on_every_write_route()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (equipmentId, rebuildId) = await SeedRebuildAsync(operatorClient);

        var anonymousClient = factory.CreateClient();

        using var uploadContent = TestUploads.Rebuild();
        var uploadResponse = await anonymousClient.PostAsync($"/equipment/{equipmentId}/rebuilds", uploadContent);
        Assert.Equal(HttpStatusCode.Unauthorized, uploadResponse.StatusCode);

        var editResponse = await anonymousClient.PutAsJsonAsync($"/rebuilds/{rebuildId}", new
        {
            rebuildDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Anonymous should not edit.",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, editResponse.StatusCode);

        using var attachContent = TestUploads.OemReport();
        var attachResponse = await anonymousClient.PostAsync($"/rebuilds/{rebuildId}/file", attachContent);
        Assert.Equal(HttpStatusCode.Unauthorized, attachResponse.StatusCode);

        var deleteResponse = await anonymousClient.DeleteAsync($"/equipment/{equipmentId}/rebuilds/{rebuildId}");
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
    }

    private static async Task<(Guid EquipmentId, Guid RebuildId)> SeedRebuildAsync(HttpClient uploaderClient)
    {
        var created = await uploaderClient.PostAsJsonAsync("/equipment", new
        {
            name = $"Permission Test Pump {Guid.NewGuid():N}",
            category = "Pump",
            serialNumber = $"PT-{Guid.NewGuid():N}"[..10],
            site = "North Pit",
        });
        var equipment = (await created.Content.ReadFromJsonAsync<CreatedEquipment>())!;

        using var content = TestUploads.Rebuild();
        var uploadResponse = await uploaderClient.PostAsync($"/equipment/{equipment.Id}/rebuilds", content);
        var rebuild = (await uploadResponse.Content.ReadFromJsonAsync<CreatedRebuild>())!;

        return (equipment.Id, rebuild.Id);
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record CreatedRebuild(Guid Id);
}
