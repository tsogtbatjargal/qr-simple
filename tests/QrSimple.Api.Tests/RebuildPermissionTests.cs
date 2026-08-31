using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

// Covers docs/plans/0002-inspection-records.md decisions 12-14 — the ones most likely to
// regress silently: Operator hard-delete, cross-Operator edit, and Reader write access.
public class InspectionPermissionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Operator_cannot_delete_an_inspection_but_admin_can()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (equipmentId, inspectionId) = await SeedInspectionAsync(operatorClient);

        var deniedResponse = await operatorClient.DeleteAsync($"/equipment/{equipmentId}/inspections/{inspectionId}");
        Assert.Equal(HttpStatusCode.Forbidden, deniedResponse.StatusCode);

        var adminClient = factory.CreateClientAs("Admin");
        var deleteResponse = await adminClient.DeleteAsync($"/equipment/{equipmentId}/inspections/{inspectionId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Operator_can_edit_a_record_they_uploaded()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (_, inspectionId) = await SeedInspectionAsync(operatorClient);

        var response = await operatorClient.PutAsJsonAsync($"/inspections/{inspectionId}", new
        {
            inspectionDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Corrected note.",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Operator_cannot_edit_another_operators_record()
    {
        var uploaderClient = factory.CreateClientAs("Operator");
        var (_, inspectionId) = await SeedInspectionAsync(uploaderClient);

        var otherOperatorClient = factory.CreateClientAs("Operator");
        var response = await otherOperatorClient.PutAsJsonAsync($"/inspections/{inspectionId}", new
        {
            inspectionDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Should not be allowed.",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_edit_any_record()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (_, inspectionId) = await SeedInspectionAsync(operatorClient);

        var adminClient = factory.CreateClientAs("Admin");
        var response = await adminClient.PutAsJsonAsync($"/inspections/{inspectionId}", new
        {
            inspectionDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Admin correction.",
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var inspection = await db.Inspections.FindAsync(inspectionId);
        Assert.NotNull(inspection!.LastEditedAtUtc);
    }

    [Fact]
    public async Task Reader_cannot_upload_edit_or_delete_an_inspection()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (equipmentId, inspectionId) = await SeedInspectionAsync(operatorClient);

        var readerClient = factory.CreateClientAs("Reader");

        using var uploadContent = TestUploads.Inspection();
        var uploadResponse = await readerClient.PostAsync($"/equipment/{equipmentId}/inspections", uploadContent);
        Assert.Equal(HttpStatusCode.Forbidden, uploadResponse.StatusCode);

        var editResponse = await readerClient.PutAsJsonAsync($"/inspections/{inspectionId}", new
        {
            inspectionDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Reader should not edit.",
        });
        Assert.Equal(HttpStatusCode.Forbidden, editResponse.StatusCode);

        var deleteResponse = await readerClient.DeleteAsync($"/equipment/{equipmentId}/inspections/{inspectionId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    [Fact]
    public async Task Anonymous_requests_are_rejected_on_every_write_route()
    {
        var operatorClient = factory.CreateClientAs("Operator");
        var (equipmentId, inspectionId) = await SeedInspectionAsync(operatorClient);

        var anonymousClient = factory.CreateClient();

        using var uploadContent = TestUploads.Inspection();
        var uploadResponse = await anonymousClient.PostAsync($"/equipment/{equipmentId}/inspections", uploadContent);
        Assert.Equal(HttpStatusCode.Unauthorized, uploadResponse.StatusCode);

        var editResponse = await anonymousClient.PutAsJsonAsync($"/inspections/{inspectionId}", new
        {
            inspectionDate = BusinessTime.Today().ToString("yyyy-MM-dd"),
            note = "Anonymous should not edit.",
        });
        Assert.Equal(HttpStatusCode.Unauthorized, editResponse.StatusCode);

        var deleteResponse = await anonymousClient.DeleteAsync($"/equipment/{equipmentId}/inspections/{inspectionId}");
        Assert.Equal(HttpStatusCode.Unauthorized, deleteResponse.StatusCode);
    }

    private static async Task<(Guid EquipmentId, Guid InspectionId)> SeedInspectionAsync(HttpClient uploaderClient)
    {
        var created = await uploaderClient.PostAsJsonAsync("/equipment", new
        {
            name = $"Permission Test Pump {Guid.NewGuid():N}",
            category = "Pump",
            serialNumber = $"PT-{Guid.NewGuid():N}"[..10],
            site = "North Pit",
        });
        var equipment = (await created.Content.ReadFromJsonAsync<CreatedEquipment>())!;

        using var content = TestUploads.Inspection();
        var uploadResponse = await uploaderClient.PostAsync($"/equipment/{equipment.Id}/inspections", content);
        var inspection = (await uploadResponse.Content.ReadFromJsonAsync<CreatedInspection>())!;

        return (equipment.Id, inspection.Id);
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record CreatedInspection(Guid Id);
}
