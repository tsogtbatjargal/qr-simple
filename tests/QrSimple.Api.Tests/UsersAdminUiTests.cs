using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrSimple.Api.Tests;

public class UsersAdminUiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_sees_the_users_list()
    {
        var client = factory.CreateClientAs("Admin");

        var response = await client.GetAsync("/app/users");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Users", body);
        Assert.Contains("Add user", body);
    }

    [Fact]
    public async Task Operator_cannot_reach_the_users_list()
    {
        var operatorEmail = $"operator-ui-{Guid.NewGuid():N}@example.com";
        var adminClient = factory.CreateClientAs("Admin");
        await adminClient.PostAsJsonAsync("/users", new { email = operatorEmail, role = "Operator" });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, operatorEmail);

        var response = await client.GetAsync("/app/users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost/app/not-authorized", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Add_user_form_renders_role_options()
    {
        var client = factory.CreateClientAs("Admin");

        var response = await client.GetAsync("/app/users/add");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Admin", body);
        Assert.Contains("Operator", body);
        Assert.Contains("Reader", body);
    }

    [Fact]
    public async Task User_detail_page_shows_email_and_role()
    {
        var client = factory.CreateClientAs("Admin");
        var created = await client.PostAsJsonAsync("/users", new
        {
            email = "detail-ui@example.com",
            role = "Operator",
        });
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();

        var response = await client.GetAsync($"/app/users/{user!.Id}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("detail-ui@example.com", body);
    }

    [Fact]
    public async Task Admin_sees_the_users_nav_link_on_the_equipment_page()
    {
        var client = factory.CreateClientAs("Admin");

        var response = await client.GetAsync("/app");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("/app/users", body);
    }

    [Fact]
    public async Task Deactivated_admin_is_redirected_away_from_the_users_page()
    {
        var deactivatedAdminEmail = $"deactivated-admin-{Guid.NewGuid():N}@example.com";
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/users", new { email = deactivatedAdminEmail, role = "Admin" });
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();
        await adminClient.PostAsync($"/users/{user!.Id}/deactivate", null);

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, deactivatedAdminEmail);

        var response = await client.GetAsync("/app/users");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost/app/not-authorized", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Deactivated_admin_does_not_see_the_users_nav_link()
    {
        var deactivatedAdminEmail = $"deactivated-admin-nav-{Guid.NewGuid():N}@example.com";
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/users", new { email = deactivatedAdminEmail, role = "Admin" });
        var user = await created.Content.ReadFromJsonAsync<UserResponse>();
        await adminClient.PostAsync($"/users/{user!.Id}/deactivate", null);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, deactivatedAdminEmail);

        var response = await client.GetAsync("/app");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("/app/users", body);
    }

    [Fact]
    public async Task Non_admin_does_not_see_the_users_nav_link()
    {
        var readerEmail = $"reader-nav-{Guid.NewGuid():N}@example.com";
        var adminClient = factory.CreateClientAs("Admin");
        await adminClient.PostAsJsonAsync("/users", new { email = readerEmail, role = "Reader" });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, readerEmail);

        var response = await client.GetAsync("/app");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("/app/users", body);
    }

    private sealed record UserResponse(Guid Id);
}
