using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace QrSimple.Api.Tests;

// The app's own login page, added 2026-08-31. Before it existed "/login" was a bare
// Results.Challenge, so POST /logout -> "/app" -> [Authorize] -> LoginPath -> "/login" walked the
// user straight out to Google's account screen: signing out looked like it had thrown you at a
// stranger's site. These tests pin the redirect chain that fixes that, since none of it is
// reachable from the Blazor render tests.
public class LoginPageTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Login_page_renders_anonymously_with_a_provider_button()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/login");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Equipment Registry", body);
        Assert.Contains("Continue with Google", body);
        Assert.Contains("/login/Google", body);
    }

    [Fact]
    public async Task Signing_out_lands_on_the_login_page_with_a_confirmation()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/logout", null);

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/login?signedOut=true", response.Headers.Location?.OriginalString);

        var page = await factory.CreateClient().GetAsync("/login?signedOut=true");
        Assert.Contains("You&#x27;ve been signed out.", await page.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Challenge_endpoint_forces_the_account_picker()
    {
        // The whole point of the login page is that sign-out means something. Without
        // prompt=select_account Google silently re-authenticates the live session, so the next
        // person on a shared field tablet lands in the previous user's account.
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/login/Google", null);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        var location = response.Headers.Location!.OriginalString;
        Assert.Contains("accounts.google.com", location);
        Assert.Contains("prompt=select_account", location);
    }

    [Fact]
    public async Task Challenge_endpoint_rejects_an_unregistered_scheme()
    {
        var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        var response = await client.PostAsync("/login/Microsoft", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    // Anything that would send the browser off this site after a successful sign-in, i.e. hand a
    // phishing page a visitor who is arriving with a freshly minted session cookie.
    [InlineData("https://evil.example/steal", "/app")]
    [InlineData("//evil.example/steal", "/app")]           // protocol-relative: relative to Uri, absolute to a browser
    [InlineData("/\\evil.example/steal", "/app")]          // backslash variant some browsers normalise to //
    [InlineData("app/equipment", "/app")]                  // unrooted, so it would resolve against whatever page we're on
    [InlineData("", "/app")]
    [InlineData(null, "/app")]
    [InlineData("/app/equipment/add", "/app/equipment/add")]
    [InlineData("/app/users?sort=email", "/app/users?sort=email")]
    public void Return_url_is_confined_to_this_site(string? returnUrl, string expected) =>
        Assert.Equal(expected, LoginProviders.SafeReturnUrl(returnUrl));

    [Fact]
    public void Every_offered_provider_names_a_scheme_and_a_known_icon() =>
        Assert.All(LoginProviders.All, provider =>
        {
            Assert.False(string.IsNullOrWhiteSpace(provider.Scheme));
            Assert.False(string.IsNullOrWhiteSpace(provider.DisplayName));
            Assert.Same(provider, LoginProviders.Find(provider.Scheme.ToLowerInvariant()));
        });
}
