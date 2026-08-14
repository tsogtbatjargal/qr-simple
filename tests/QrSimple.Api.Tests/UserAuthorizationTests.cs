using System.Net;
using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class UserAuthorizationTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Anonymous_request_is_rejected_once_an_admin_exists()
    {
        factory.CreateClientAs("Admin");
        var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync("/users", new
        {
            email = "new-operator@example.com",
            role = "Operator",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_request_is_rejected_once_an_admin_exists()
    {
        factory.CreateClientAs("Admin");
        var client = factory.CreateClientAs("Operator");

        var response = await client.PostAsJsonAsync("/users", new
        {
            email = "new-reader@example.com",
            role = "Reader",
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_add_additional_users_once_an_admin_exists()
    {
        var adminClient = factory.CreateClientAs("Admin");

        var response = await adminClient.PostAsJsonAsync("/users", new
        {
            email = "new-operator2@example.com",
            role = "Operator",
        });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
}
