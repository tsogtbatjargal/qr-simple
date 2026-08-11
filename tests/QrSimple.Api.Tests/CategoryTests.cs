using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class CategoryTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_can_add_a_category_and_see_it_in_the_list()
    {
        var client = factory.CreateClientAs("Admin");

        var addResponse = await client.PostAsJsonAsync("/categories", new { name = "Excavator" });
        Assert.True(addResponse.IsSuccessStatusCode);

        var listResponse = await client.GetAsync("/categories");
        var categories = await listResponse.Content.ReadFromJsonAsync<List<CategoryResponse>>();

        Assert.Contains(categories!, c => c.Name == "Excavator");
    }

    [Fact]
    public async Task Creating_equipment_with_unknown_category_is_rejected()
    {
        var client = factory.CreateClientAs("Operator");

        var response = await client.PostAsJsonAsync("/equipment", new
        {
            name = "Mystery Machine",
            category = "NotARealCategory",
            serialNumber = "MM-0001",
            site = "North Pit",
        });

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record CategoryResponse(Guid Id, string Name);
}
