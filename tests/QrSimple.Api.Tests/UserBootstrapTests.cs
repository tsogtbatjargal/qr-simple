using System.Net;
using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class UserBootstrapTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anonymous_request_can_create_the_first_admin()
    {
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/users", new
        {
            email = "first-admin@example.com",
            role = "Admin",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
