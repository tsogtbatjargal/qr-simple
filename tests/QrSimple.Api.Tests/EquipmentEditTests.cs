using System.Net;
using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class EquipmentEditTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_can_edit_all_fields_of_an_existing_equipment_record()
    {
        var adminClient = factory.CreateClientAs("Admin");

        var createResponse = await adminClient.PostAsJsonAsync("/equipment", new
        {
            name = "Loader 5",
            category = "Loader",
            serialNumber = "LD-0005",
            site = "North Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var editResponse = await adminClient.PutAsJsonAsync($"/equipment/{created!.Id}", new
        {
            name = "Loader 5 (Renamed)",
            category = "Loader",
            serialNumber = "LD-0005-B",
            site = "South Pit",
        });
        Assert.True(editResponse.IsSuccessStatusCode);

        var scanResponse = await adminClient.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.Contains("Loader 5 (Renamed)", html);
        Assert.Contains("South Pit", html);
    }

    [Fact]
    public async Task Operator_can_edit_category_and_site()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Loader 6",
            category = "Loader",
            serialNumber = "LD-0006",
            site = "North Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var editResponse = await client.PutAsJsonAsync($"/equipment/{created!.Id}", new
        {
            name = "Loader 6",
            category = "Truck",
            serialNumber = "LD-0006",
            site = "South Pit",
        });
        Assert.True(editResponse.IsSuccessStatusCode);

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();

        Assert.Contains("South Pit", html);
        Assert.Contains("Truck", html);
    }

    [Fact]
    public async Task Operator_cannot_edit_name_or_serial_number()
    {
        var client = factory.CreateClientAs("Operator");

        var createResponse = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Loader 7",
            category = "Loader",
            serialNumber = "LD-0007",
            site = "North Pit",
        });
        var created = await createResponse.Content.ReadFromJsonAsync<CreatedEquipment>();

        var editResponse = await client.PutAsJsonAsync($"/equipment/{created!.Id}", new
        {
            name = "Loader 7 (Renamed)",
            category = "Loader",
            serialNumber = "LD-0007",
            site = "North Pit",
        });

        Assert.Equal(HttpStatusCode.Forbidden, editResponse.StatusCode);

        var scanResponse = await client.GetAsync($"/e/{created.Id}");
        var html = await scanResponse.Content.ReadAsStringAsync();
        Assert.Contains("Loader 7</h1>", html);
        Assert.DoesNotContain("Loader 7 (Renamed)", html);
    }

    private sealed record CreatedEquipment(Guid Id);
}
