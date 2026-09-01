using System.Net;

namespace QrSimple.Api;

public static class ScanPage
{
    /// <summary>
    /// Lucide icons (ISC, https://lucide.dev), inlined so they inherit currentColor.
    /// These mirror <c>Components/Shared/Icon.razor</c> — the admin UI and this page are
    /// one design system, so an icon added on one side belongs on the other.
    /// </summary>
    private const string FileTextIcon =
        """<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z"/><path d="M14 2v5a1 1 0 0 0 1 1h5"/><path d="M10 9H8"/><path d="M16 13H8"/><path d="M16 17H8"/></svg>""";

    private const string ExternalLinkIcon =
        """<svg class="icon arrow" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h6"/></svg>""";

    private const string AlertIcon =
        """<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><circle cx="12" cy="12" r="10"/><path d="m15 9-6 6"/><path d="m9 9 6 6"/></svg>""";

    private const string WrenchIcon =
        """<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M14.7 6.3a1 1 0 0 0 0 1.4l1.6 1.6a1 1 0 0 0 1.4 0l3.77-3.77a6 6 0 0 1-7.94 7.94l-6.91 6.91a2.12 2.12 0 0 1-3-3l6.91-6.91a6 6 0 0 1 7.94-7.94l-3.76 3.76z"/></svg>""";

    private const string ShieldCheckIcon =
        """<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M20 13c0 5-3.5 7.5-7.66 8.95a1 1 0 0 1-.67-.01C7.5 20.5 4 18 4 13V6a1 1 0 0 1 1-1c2 0 4.5-1.2 6.24-2.72a1.17 1.17 0 0 1 1.52 0C14.51 3.81 17 5 19 5a1 1 0 0 1 1 1z"/><path d="m9 12 2 2 4-4"/></svg>""";

    public static string Render(Equipment equipment, IReadOnlyList<Document> documents, int rebuildCount)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);

        var photo = documents.FirstOrDefault(document =>
            document.Label.Equals("Equipment Photo", StringComparison.OrdinalIgnoreCase) ||
            document.Label.Equals("Equipment Image", StringComparison.OrdinalIgnoreCase));

        var retiredNotice = equipment.Status == EquipmentStatus.Retired
            ? $"""<div class="retired" role="status">{AlertIcon}<span>This equipment is no longer in service.</span></div>"""
            : "";

        var equipmentImage = photo switch
        {
            { Content: not null } => $"""<img src="/documents/{photo.Id}/content" alt="{Encode(equipment.Name)}">""",
            { Url: not null } => $"""<img src="{Encode(photo.Url)}" alt="{Encode(equipment.Name)}">""",
            _ => """
                <svg class="placeholder" viewBox="0 0 240 150" role="img" aria-label="Equipment image not available">
                    <rect x="24" y="71" width="125" height="48" rx="10" fill="currentColor" opacity=".2"/>
                    <circle cx="64" cy="125" r="18" fill="currentColor"/>
                    <circle cx="137" cy="125" r="18" fill="currentColor"/>
                    <path d="M149 82h26l26 24v13h-52zM37 70l24-31h60l19 31z" fill="currentColor"/>
                    <path d="M166 44h13v40h-13zM158 35h30v13h-30z" fill="currentColor" opacity=".75"/>
                </svg>
                """,
        };

        var oemReport = documents.FirstOrDefault(document => DocumentCatalog.IsOemReportLabel(document.Label));

        var documentLinks = string.Concat(documents
            .Where(document => document != photo && document != oemReport)
            // These two strings are stored Document.Label *data*, not UI copy — they match what
            // someone typed or what was derived from a filename, and rows carrying them already
            // exist in the database. Do not sweep them into a UI rename: the admin section that
            // lists these was "Documents", then "User Manual", and is "Manuals" as of 2026-09-01,
            // and none of those renames may touch these, or every existing "User manual" row
            // silently falls to the bottom of the scan page's order with nothing to show why.
            .OrderBy(document => document.Label.Equals("User manual", StringComparison.OrdinalIgnoreCase) ? 0
                : document.Label.Equals("Maintenance instruction", StringComparison.OrdinalIgnoreCase) ? 1
                : 2)
            .ThenBy(document => document.Label)
            .Select(document =>
            {
                var href = document.Content is not null ? $"/documents/{document.Id}/content" : document.Url;
                return $"""
                    <a class="panel document" href="{Encode(href ?? "")}" target="_blank" rel="noopener noreferrer">
                        {FileTextIcon}<span>{Encode(document.Label)}</span>{ExternalLinkIcon}
                    </a>
                    """;
            }));

        // Count is the true total (no cap — see docs/plans/0002-inspection-records.md decision
        // 18). Rendered only when non-zero: a technician tapping into an empty rebuild history
        // page is worse than not offering the link at all.
        var rebuildsPanel = rebuildCount > 0
            ? $"""
                <a class="panel document" href="/e/{equipment.Id}/rebuilds">
                    {WrenchIcon}<span>Rebuild history ({rebuildCount})</span>{ExternalLinkIcon}
                </a>
                """
            : "";

        // One per equipment, so this is a direct link to the PDF rather than a list page.
        var oemReportPanel = oemReport is not null
            ? $"""
                <a class="panel document" href="{Encode(oemReport.Content is not null ? $"/documents/{oemReport.Id}/content" : oemReport.Url ?? "")}" target="_blank" rel="noopener noreferrer">
                    {ShieldCheckIcon}<span>OEM QA/QC report</span>{ExternalLinkIcon}
                </a>
                """
            : "";

        // "documents" here is a genuine collective — this nav holds the user manual and anything
        // filed beside it, *plus* the OEM QA/QC report and the rebuild-history link. So it keeps
        // that wording even though the admin page's matching section is called "Manuals"
        // (renamed from "Documents" on 2026-08-31, then to "Manuals" on 2026-09-01): narrowing the
        // aria-label or the empty state to "manuals" would claim no manual exists when what is
        // actually missing is all three. Don't "fix" this to match.
        var documentsSection = documentLinks.Length > 0 || rebuildsPanel.Length > 0 || oemReportPanel.Length > 0
            ? $"""<nav class="documents" aria-label="Equipment documents">{documentLinks}{oemReportPanel}{rebuildsPanel}</nav>"""
            : """<p class="empty-documents">No documents are available for this equipment.</p>""";

        // Colours, radii, shadows and the typeface all come from brand/tokens.css, linked
        // in the head below. This page and the admin UI (wwwroot/app.css) are one shared
        // design system — never introduce a literal colour on one surface alone. Head tags,
        // the site header, and the base :root/body/.icon/.site-header/main/.panel rules live
        // in PublicPageChrome, shared with RebuildsPage.cs.
        var styles = PublicPageChrome.BaseStyles + """
            .stack { display: grid; gap: 14px; }
            .photo { min-height: 220px; display: grid; place-items: center; padding: 18px; background: var(--brand-primary); border-radius: 18px; box-shadow: var(--brand-shadow-md); }
            .photo-frame { width: min(100%, 340px); aspect-ratio: 16 / 10; display: grid; grid-template-rows: 1fr; grid-template-columns: 1fr; place-items: center; overflow: hidden; background: var(--brand-surface); border-radius: 12px; padding: 16px; }
            .photo img { width: 100%; height: 100%; min-width: 0; min-height: 0; object-fit: contain; }
            .placeholder { width: 74%; max-height: 78%; color: var(--brand-primary); }
            h1 { margin: 0; font-size: clamp(1.55rem, 6vw, 2.25rem); line-height: 1.15; font-weight: 700; }
            .documents { display: grid; gap: 14px; }
            .document { position: relative; gap: 14px; padding-inline: 56px; font-size: clamp(1.2rem, 5vw, 1.65rem); font-weight: 600; text-decoration: none; transition: transform .15s ease, background .15s ease; }
            .document .icon { font-size: 1.1em; }
            .document:hover { background: var(--brand-primary-dark); transform: translateY(-1px); }
            .document:focus-visible { outline: 4px solid var(--brand-navy); outline-offset: 3px; }
            .arrow { position: absolute; right: 22px; }
            .retired { display: flex; align-items: center; justify-content: center; gap: 10px; padding: 16px 20px; border: 2px solid var(--brand-danger); border-radius: 12px; background: var(--brand-danger-bg); color: var(--brand-danger-dark); text-align: center; font-weight: 700; }
            .retired .icon { font-size: 1.3rem; }
            .details { margin-top: 22px; padding: 22px; border-radius: 14px; background: var(--brand-surface); border: 1px solid var(--brand-border); box-shadow: var(--brand-shadow); }
            .details h2 { margin: 0 0 14px; font-size: 1.15rem; color: var(--brand-navy); }
            dl { display: grid; grid-template-columns: max-content 1fr; gap: 12px 18px; margin: 0; }
            dt { color: var(--brand-muted); font-weight: 600; }
            dd { margin: 0; overflow-wrap: anywhere; }
            .empty-documents { margin: 0; padding: 18px; border-radius: 12px; background: var(--brand-surface); border: 1px solid var(--brand-border); color: var(--brand-muted); text-align: center; }
            @media (prefers-reduced-motion: reduce) {
                .document { transition: none; }
                .document:hover { transform: none; }
            }
            @media (max-width: 480px) {
                .stack, .documents { gap: 10px; }
                .photo { min-height: 190px; border-radius: 12px; }
                .photo-frame { padding: 10px; }
                .document { padding-inline: 46px; gap: 10px; }
                .arrow { right: 16px; }
                dl { grid-template-columns: 1fr; gap: 4px; }
                dd + dt { margin-top: 10px; }
            }
            """;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
                {PublicPageChrome.HeadTags(Encode(equipment.Name))}
                <style>{styles}</style>
            </head>
            <body>
                {PublicPageChrome.Header}
                <main>
                    <div class="stack">
                        <section class="photo" aria-label="Equipment photo">
                            <div class="photo-frame">{equipmentImage}</div>
                        </section>
                        <section class="panel"><h1>{Encode(equipment.Name)}</h1></section>
                        {retiredNotice}
                        {documentsSection}
                    </div>
                    <section class="details" aria-labelledby="details-heading">
                        <h2 id="details-heading">Equipment details</h2>
                        <dl>
                            <dt>Category</dt><dd>{Encode(equipment.Category)}</dd>
                            <dt>Serial/Asset Number</dt><dd>{Encode(equipment.SerialNumber)}</dd>
                            <dt>Site</dt><dd>{Encode(equipment.Site)}</dd>
                            <dt>Status</dt><dd>{Encode(equipment.Status.ToString())}</dd>
                        </dl>
                    </section>
                </main>
            </body>
            </html>
            """;
    }
}
