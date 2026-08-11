using System.Net;
using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class EquipmentTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Operator_can_create_a_single_equipment_record()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Haul Truck 12",
            category = "Truck",
            serialNumber = "HT-0012",
            site = "North Pit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<EquipmentResponse>();
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Haul Truck 12", created.Name);
        Assert.Equal("Truck", created.Category);
        Assert.Equal("HT-0012", created.SerialNumber);
        Assert.Equal("North Pit", created.Site);
        Assert.Equal("Active", created.Status);
    }

    private sealed record EquipmentResponse(
        Guid Id,
        string Name,
        string Category,
        string SerialNumber,
        string Site,
        string Status);
}
