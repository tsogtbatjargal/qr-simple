using System.Net;
using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class EquipmentListTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Unauthenticated_request_cannot_list_equipment()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/equipment");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Reader_can_list_equipment_and_retired_records_are_hidden_by_default()
    {
        var operatorClient = factory.CreateClientAs("Operator");

        var active = await operatorClient.PostAsJsonAsync("/equipment", new
        {
            name = "Active Truck",
            category = "Truck",
            serialNumber = "AT-0001",
            site = "North Pit",
        });
        var activeCreated = await active.Content.ReadFromJsonAsync<CreatedEquipment>();

        var retired = await operatorClient.PostAsJsonAsync("/equipment", new
        {
            name = "Retired Truck",
            category = "Truck",
            serialNumber = "RT-0002",
            site = "North Pit",
        });
        var retiredCreated = await retired.Content.ReadFromJsonAsync<CreatedEquipment>();
        await operatorClient.PostAsync($"/equipment/{retiredCreated!.Id}/retire", content: null);

        var readerClient = factory.CreateClientAs("Reader");
        var listResponse = await readerClient.GetAsync("/equipment");

        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);
        var equipment = await listResponse.Content.ReadFromJsonAsync<List<ListedEquipment>>();

        Assert.Contains(equipment!, e => e.Id == activeCreated!.Id);
        Assert.DoesNotContain(equipment!, e => e.Id == retiredCreated.Id);
    }

    [Fact]
    public async Task IncludeRetired_filter_reveals_retired_equipment()
    {
        var operatorClient = factory.CreateClientAs("Operator");

        var retired = await operatorClient.PostAsJsonAsync("/equipment", new
        {
            name = "Old Conveyor",
            category = "Conveyor",
            serialNumber = "OC-0003",
            site = "North Pit",
        });
        var retiredCreated = await retired.Content.ReadFromJsonAsync<CreatedEquipment>();
        await operatorClient.PostAsync($"/equipment/{retiredCreated!.Id}/retire", content: null);

        var listResponse = await operatorClient.GetAsync("/equipment?includeRetired=true");
        var equipment = await listResponse.Content.ReadFromJsonAsync<List<ListedEquipment>>();

        Assert.Contains(equipment!, e => e.Id == retiredCreated.Id);
    }

    private sealed record CreatedEquipment(Guid Id);
    private sealed record ListedEquipment(Guid Id, string Name, string Status);
}
