using System.Net;
using System.Net.Http.Json;

namespace QrSimple.Api.Tests;

public class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Registered_user_can_see_their_own_email_and_role()
    {
        var client = factory.CreateClient();

        await client.PostAsJsonAsync("/users", new { email = "reader@example.com", role = "Reader" });

        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, "reader@example.com");
        var response = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var me = await response.Content.ReadFromJsonAsync<MeResponse>();
        Assert.Equal("reader@example.com", me!.Email);
        Assert.Equal("Reader", me.Role);
    }

    [Fact]
    public async Task Unregistered_email_gets_a_clear_not_authorized_message()
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, "stranger@example.com");

        var response = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("not authorized", body, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contact your admin", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Unauthenticated_request_is_rejected()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record MeResponse(string Email, string Role);
}
