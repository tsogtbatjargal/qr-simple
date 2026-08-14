using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class UserManagementTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Duplicate_email_is_rejected_on_create()
    {
        var adminClient = factory.CreateClientAs("Admin");
        const string email = "duplicate-create@example.com";

        var first = await adminClient.PostAsJsonAsync("/users", new { email, role = "Operator" });
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await adminClient.PostAsJsonAsync("/users", new { email, role = "Reader" });
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Unknown_role_is_rejected_on_create()
    {
        var adminClient = factory.CreateClientAs("Admin");

        var response = await adminClient.PostAsJsonAsync("/users", new
        {
            email = "unknown-role@example.com",
            role = "SuperAdmin",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_update_a_users_role()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/users", new
        {
            email = "role-update@example.com",
            role = "Operator",
        });
        var user = await created.Content.ReadFromJsonAsync<UserDto>();

        var response = await adminClient.PutAsJsonAsync($"/users/{user!.Id}", new { role = "Reader" });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal("Reader", updated!.Role);
    }

    [Fact]
    public async Task Updating_to_an_unknown_role_is_rejected()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/users", new
        {
            email = "role-update-bad@example.com",
            role = "Operator",
        });
        var user = await created.Content.ReadFromJsonAsync<UserDto>();

        var response = await adminClient.PutAsJsonAsync($"/users/{user!.Id}", new { role = "SuperAdmin" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Admin_can_deactivate_and_reactivate_a_user()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var created = await adminClient.PostAsJsonAsync("/users", new
        {
            email = "deactivate-me@example.com",
            role = "Operator",
        });
        var user = await created.Content.ReadFromJsonAsync<UserDto>();

        var deactivate = await adminClient.PostAsync($"/users/{user!.Id}/deactivate", null);
        Assert.Equal(HttpStatusCode.OK, deactivate.StatusCode);

        var deactivatedClient = factory.CreateClient();
        deactivatedClient.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, "deactivate-me@example.com");
        var equipmentResponse = await deactivatedClient.GetAsync("/equipment");
        Assert.Equal(HttpStatusCode.Forbidden, equipmentResponse.StatusCode);

        var reactivate = await adminClient.PostAsync($"/users/{user.Id}/reactivate", null);
        Assert.Equal(HttpStatusCode.OK, reactivate.StatusCode);

        var reactivatedResponse = await deactivatedClient.GetAsync("/equipment");
        Assert.Equal(HttpStatusCode.OK, reactivatedResponse.StatusCode);
    }

    [Fact]
    public async Task Admin_cannot_deactivate_their_own_account()
    {
        var adminClient = factory.CreateClientAs("Admin");
        var meResponse = await adminClient.GetAsync("/me");
        var me = await meResponse.Content.ReadFromJsonAsync<MeDto>();

        Guid adminId;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            adminId = db.Users.Single(u => u.Email == me!.Email).Id;
        }

        var response = await adminClient.PostAsync($"/users/{adminId}/deactivate", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record UserDto(Guid Id, string Email, string Role, bool IsActive);
    private sealed record MeDto(string Email, string Role);
}
