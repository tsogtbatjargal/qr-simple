using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class EquipmentEditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Operator_can_edit_an_existing_equipment_record()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Loader 5",
            category = "Loader",
            serialNumber = "LD-0005",
            site = "North Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var editResponse = await client.PutAsJsonAsync($"/equipment/{created!.Id}", new
        {
            name = "Loader 5 (Renamed)",
            category = "Loader",
            serialNumber = "LD-0005",
            site = "South Pit",
        });
        Assert.True(editResponse.IsSuccessStatusCode);

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.Contains("Loader 5 (Renamed)", html);
        Assert.Contains("South Pit", html);
    }

    private sealed record CreatedEquipment(Guid Id);
}
