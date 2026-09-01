namespace QrSimple.Api;

// ScanPage.cs (/e/{id}) and RebuildsPage.cs (/e/{id}/rebuilds) are two renderings of one
// public design system, not independent pages — this holds what they share (head tags, the
// site header, and the base CSS for :root/body/.icon/.site-header/main/.panel) so a change to
// either lands in one place instead of drifting apart. Each page still owns its own
// page-specific CSS and composes it after BaseStyles in its own <style> block.
public static class PublicPageChrome
{
    // `title` must already be HTML-encoded by the caller (see ScanPage/RebuildsPage's local
    // Encode helper) — this class doesn't know the source value well enough to encode it itself.
    public static string HeadTags(string title) => $"""
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{title}</title>
        <meta name="theme-color" content="#156fc1">
        <link rel="icon" type="image/png" sizes="32x32" href="/brand/logos/favicon-32.png">
        <link rel="apple-touch-icon" href="/brand/logos/apple-touch-icon.png">
        <link rel="stylesheet" href="/brand/tokens.css">
        """;

    public const string Header = """
        <header class="site-header">
            <img src="/brand/logos/ics-logo.png" alt="ICS Mongolia" width="1381" height="678">
            <span class="product">Equipment Registry</span>
        </header>
        """;

    // The header centres its logo and product name at every width, matching the admin header
    // in app.css — both used to hug the left edge above 600px and only centre on phones, which
    // made one product look like two different layouts depending on the device.
    public const string BaseStyles = """
        :root { color-scheme: light; font-family: var(--brand-font); }
        * { box-sizing: border-box; }
        body { margin: 0; background: var(--brand-bg); color: var(--brand-text); }
        .icon { width: 1em; height: 1em; flex: 0 0 auto; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round; stroke-linejoin: round; }
        .site-header { display: flex; align-items: center; justify-content: center; gap: 12px; padding: 12px clamp(12px, 3vw, 24px); background: var(--brand-surface); border-bottom: 1px solid var(--brand-border); box-shadow: var(--brand-shadow-sm); }
        .site-header img { height: 30px; width: auto; display: block; }
        .site-header .product { font-weight: 700; font-size: .95rem; color: var(--brand-navy); letter-spacing: -0.01em; }
        main { width: min(100%, 760px); min-height: 100vh; margin: 0 auto; padding: clamp(12px, 3vw, 24px); }
        .panel { min-height: 84px; display: flex; align-items: center; justify-content: center; padding: 20px; border: 0; border-radius: 14px; background: var(--brand-primary); color: white; text-align: center; box-shadow: var(--brand-shadow-md); }
        @media (max-width: 480px) {
            main { padding: 10px; }
            .panel { min-height: 76px; border-radius: 10px; }
        }
        """;
}
