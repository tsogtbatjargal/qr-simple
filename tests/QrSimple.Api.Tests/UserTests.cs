using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class UserTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_can_add_a_user_and_see_it_in_the_list()
    {
        var client = factory.CreateClientAs("Admin");

        var addResponse = await client.PostAsJsonAsync("/users", new
        {
            email = "operator@example.com",
            role = "Operator",
        });
        Assert.True(addResponse.IsSuccessStatusCode);

        var listResponse = await client.GetAsync("/users");
        var users = await listResponse.Content.ReadFromJsonAsync<List<UserResponse>>();

        Assert.Contains(users!, u => u.Email == "operator@example.com" && u.Role == "Operator");
    }

    private sealed record UserResponse(Guid Id, string Email, string Role);
}
