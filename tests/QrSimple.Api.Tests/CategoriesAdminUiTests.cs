using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrSimple.Api.Tests;

public class CategoriesAdminUiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Admin_sees_the_categories_list()
    {
        var client = factory.CreateClientAs("Admin");

        var response = await client.GetAsync("/app/categories");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Categories", body);
        Assert.Contains("Add category", body);
    }

    [Fact]
    public async Task Operator_cannot_reach_the_categories_list()
    {
        var operatorEmail = $"operator-cat-ui-{Guid.NewGuid():N}@example.com";
        var adminClient = factory.CreateClientAs("Admin");
        await adminClient.PostAsJsonAsync("/users", new { email = operatorEmail, role = "Operator" });

        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, operatorEmail);

        var response = await client.GetAsync("/app/categories");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("http://localhost/app/not-authorized", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task Add_category_form_renders()
    {
        var client = factory.CreateClientAs("Admin");

        var response = await client.GetAsync("/app/categories/add");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Add category", body);
    }

    [Fact]
    public async Task Admin_sees_the_categories_nav_link()
    {
        var client = factory.CreateClientAs("Admin");

        var response = await client.GetAsync("/app");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Contains("/app/categories", body);
    }

    [Fact]
    public async Task Non_admin_does_not_see_the_categories_nav_link()
    {
        var readerEmail = $"reader-cat-nav-{Guid.NewGuid():N}@example.com";
        var adminClient = factory.CreateClientAs("Admin");
        await adminClient.PostAsJsonAsync("/users", new { email = readerEmail, role = "Reader" });

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add(TestAuthHandler.TestEmailHeader, readerEmail);

        var response = await client.GetAsync("/app");
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("/app/categories", body);
    }
}
