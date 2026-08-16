using System.Net;
using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class RoleGatingTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Unauthenticated_request_cannot_create_equipment()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Unauthorized Truck",
            category = "Truck",
            serialNumber = "UT-0001",
            site = "North Pit",
        });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reader_cannot_create_equipment()
    {
        var client = factory.CreateClientAs("Reader");

        var response = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Reader Truck",
            category = "Truck",
            serialNumber = "RT-0001",
            site = "North Pit",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Operator_can_create_equipment()
    {
        var client = factory.CreateClientAs("Operator");

        var response = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Operator Truck",
            category = "Truck",
            serialNumber = "OT-0001",
            site = "North Pit",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Operator_cannot_retire_or_reactivate_equipment()
    {
        var client = factory.CreateClientAs("Operator");

        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Status Guarded Truck",
            category = "Truck",
            serialNumber = "SG-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<CreatedEquipment>();

        var retireResponse = await client.PostAsync($"/equipment/{equipment!.Id}/retire", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, retireResponse.StatusCode);

        var reactivateResponse = await client.PostAsync($"/equipment/{equipment.Id}/reactivate", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, reactivateResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_can_retire_and_reactivate_equipment()
    {
        var client = factory.CreateClientAs("Admin");

        var created = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Admin Status Truck",
            category = "Truck",
            serialNumber = "AS-0001",
            site = "North Pit",
        });
        var equipment = await created.Content.ReadFromJsonAsync<CreatedEquipment>();

        var retireResponse = await client.PostAsync($"/equipment/{equipment!.Id}/retire", content: null);
        Assert.True(retireResponse.IsSuccessStatusCode);

        var reactivateResponse = await client.PostAsync($"/equipment/{equipment.Id}/reactivate", content: null);
        Assert.True(reactivateResponse.IsSuccessStatusCode);
    }

    private sealed record CreatedEquipment(Guid Id);
}
