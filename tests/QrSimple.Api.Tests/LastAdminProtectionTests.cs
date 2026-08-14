using System.Net;
using System.Net.Http.Json;
using Microsoft.Extensions.DependencyInjection;

namespace QrSimple.Api.Tests;

public class LastAdminProtectionTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Demoting_the_last_admin_is_rejected()
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

        var response = await adminClient.PutAsJsonAsync($"/users/{adminId}", new { role = "Operator" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed record MeDto(string Email, string Role);
}
