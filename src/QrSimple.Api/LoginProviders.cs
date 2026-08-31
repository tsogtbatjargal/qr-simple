using Microsoft.AspNetCore.Authentication.Google;

namespace QrSimple.Api;

/// <summary>
/// One sign-in button on the login page (<c>Components/Pages/Login.razor</c>).
/// </summary>
/// <param name="Scheme">
/// The ASP.NET Core authentication scheme name handed to <c>Results.Challenge</c>. It doubles as
/// the URL segment in <c>/login/{scheme}</c>, so it must stay URL-safe, and it must match a scheme
/// actually registered in <c>Program.cs</c> — otherwise the button 404s.
/// </param>
/// <param name="DisplayName">Button label. Names the provider, so the chrome doesn't have to.</param>
/// <param name="Icon">A name <c>Components/Shared/Icon.razor</c> knows.</param>
public sealed record LoginProvider(string Scheme, string DisplayName, string Icon);

public static class LoginProviders
{
    // Exactly one entry today. docs/plans/0003-pluggable-authentication-provider.md decision 6
    // assumed one scheme per deployment and therefore no picker page; this list supersedes that
    // half of the decision (see the amendment note at the top of the plan). Adding Microsoft/Entra
    // is a second entry here plus the matching .AddOpenIdConnect(...) registration in Program.cs —
    // Login.razor just loops over All and never needs editing.
    //
    // Google's brand mark is not among the inlined Lucide icons and would be the only multi-colour
    // icon in the set, so the generic "log-in" glyph carries the button instead; DisplayName is
    // what actually identifies the provider.
    public static readonly LoginProvider[] All =
    [
        new(GoogleDefaults.AuthenticationScheme, "Continue with Google", "log-in"),
    ];

    public static LoginProvider? Find(string scheme) =>
        All.FirstOrDefault(provider => string.Equals(provider.Scheme, scheme, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Confines a caller-supplied <c>returnUrl</c> to this site, falling back to <c>/app</c>.
    /// </summary>
    /// <remarks>
    /// returnUrl reaches the challenge endpoint straight from the query string, and the OAuth
    /// round-trip lands the browser on it afterwards. Unfiltered, <c>/login/Google?returnUrl=https://evil.example</c>
    /// would turn this domain into an open redirect that arrives *carrying a freshly issued session
    /// cookie* — which is exactly the moment a phishing page wants a user. The rules: relative,
    /// rooted at <c>/</c>, and not protocol-relative (<c>//evil.example</c> is a relative URI as far
    /// as <see cref="Uri.IsWellFormedUriString"/> is concerned, but browsers treat it as absolute).
    /// Lives here rather than in Program.cs so it is directly unit-testable.
    /// </remarks>
    public static string SafeReturnUrl(string? returnUrl) =>
        !string.IsNullOrEmpty(returnUrl)
        && returnUrl.StartsWith('/')
        && !returnUrl.StartsWith("//", StringComparison.Ordinal)
        && !returnUrl.StartsWith("/\\", StringComparison.Ordinal)
        && Uri.IsWellFormedUriString(returnUrl, UriKind.Relative)
            ? returnUrl
            : "/app";
}
