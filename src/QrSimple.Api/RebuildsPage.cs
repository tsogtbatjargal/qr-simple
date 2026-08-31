using System.Globalization;
using System.Net;

namespace QrSimple.Api;

public static class InspectionsPage
{
    /// <summary>
    /// Lucide icons (ISC, https://lucide.dev) — mirrors ScanPage.cs and
    /// Components/Shared/Icon.razor; an icon added here belongs on both.
    /// </summary>
    private const string FileTextIcon =
        """<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M6 22a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h8a2.4 2.4 0 0 1 1.704.706l3.588 3.588A2.4 2.4 0 0 1 20 8v12a2 2 0 0 1-2 2z"/><path d="M14 2v5a1 1 0 0 0 1 1h5"/><path d="M10 9H8"/><path d="M16 13H8"/><path d="M16 17H8"/></svg>""";

    private const string ExternalLinkIcon =
        """<svg class="icon arrow" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="M15 3h6v6"/><path d="M10 14 21 3"/><path d="M18 13v6a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h6"/></svg>""";

    private const string ArrowLeftIcon =
        """<svg class="icon" viewBox="0 0 24 24" aria-hidden="true" focusable="false"><path d="m12 19-7-7 7-7"/><path d="M19 12H5"/></svg>""";

    // `today` is a parameter, not read inside, so the recent/older split is testable without
    // mocking the clock or business timezone.
    public static string Render(Equipment equipment, IReadOnlyList<InspectionListItem> inspections, DateOnly today)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);

        string Row(InspectionListItem item)
        {
            var noteHtml = string.IsNullOrWhiteSpace(item.Note)
                ? ""
                : $"""<p class="inspection-note">{Encode(item.Note)}</p>""";

            return $"""
                <article class="inspection-row">
                    <div class="inspection-row-top">
                        <span class="inspection-date">{item.InspectionDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}</span>
                        <span class="badge">{Encode(InspectionKinds.DisplayName(item.Kind))}</span>
                    </div>
                    {noteHtml}
                    <a class="open-pdf" href="/inspections/{item.Id}/content" target="_blank" rel="noopener noreferrer">
                        {FileTextIcon}<span>Open PDF</span>{ExternalLinkIcon}
                    </a>
                </article>
                """;
        }

        var (recent, older) = InspectionCatalog.SplitByRecency(inspections, today, item => item.InspectionDate);

        var listBody = inspections.Count == 0
            ? """<p class="empty-documents">No inspection records yet.</p>"""
            : $"""
                <div class="inspection-list">{string.Concat(recent.Select(Row))}</div>
                {(older.Count > 0
                    ? $"""
                        <details class="older-inspections">
                            <summary>Older inspections ({older.Count})</summary>
                            <div class="inspection-list">{string.Concat(older.Select(Row))}</div>
                        </details>
                        """
                    : "")}
                """;

        // Head, header, and the shared :root/body/.icon/.site-header/main/.panel rules live in
        // PublicPageChrome, alongside ScanPage.cs — never introduce a literal colour on this
        // surface, reuse the var(--brand-*) tokens from tokens.css.
        var styles = PublicPageChrome.BaseStyles + """
            .back-link { display: inline-flex; align-items: center; gap: 8px; margin-bottom: 14px; color: var(--brand-navy); font-weight: 600; text-decoration: none; }
            .back-link:hover { text-decoration: underline; }
            .back-link:focus-visible { outline: 3px solid var(--brand-navy); outline-offset: 3px; }
            h1 { margin: 0; font-size: clamp(1.4rem, 5vw, 2rem); line-height: 1.2; font-weight: 700; }
            .subheading { margin: 6px 0 20px; color: var(--brand-muted); font-weight: 600; }
            .inspection-list { display: grid; gap: 12px; margin: 0 0 18px; }
            .inspection-row { padding: 16px 18px; border-radius: 14px; background: var(--brand-surface); border: 1px solid var(--brand-border); box-shadow: var(--brand-shadow); }
            .inspection-row-top { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
            .inspection-date { font-size: 1.15rem; font-weight: 700; color: var(--brand-text); }
            .badge { padding: 4px 12px; border-radius: 999px; background: var(--brand-primary); color: white; font-size: .8rem; font-weight: 700; text-transform: uppercase; letter-spacing: .02em; }
            .inspection-note { margin: 10px 0 0; color: var(--brand-text); overflow-wrap: anywhere; }
            .open-pdf { display: inline-flex; align-items: center; gap: 8px; margin-top: 12px; color: var(--brand-primary); font-weight: 700; text-decoration: none; }
            .open-pdf:hover { text-decoration: underline; }
            .open-pdf:focus-visible { outline: 3px solid var(--brand-navy); outline-offset: 3px; }
            .older-inspections summary { cursor: pointer; padding: 14px 18px; border-radius: 14px; background: var(--brand-surface); border: 1px solid var(--brand-border); font-weight: 700; color: var(--brand-navy); margin-bottom: 12px; }
            .older-inspections summary:focus-visible { outline: 3px solid var(--brand-navy); outline-offset: 3px; }
            .older-inspections[open] summary { margin-bottom: 12px; }
            .empty-documents { margin: 0; padding: 18px; border-radius: 12px; background: var(--brand-surface); border: 1px solid var(--brand-border); color: var(--brand-muted); text-align: center; }
            @media (max-width: 480px) {
                .inspection-row { padding: 12px 14px; }
                .inspection-date { font-size: 1.05rem; }
            }
            """;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
                {PublicPageChrome.HeadTags(Encode($"{equipment.Name} — Inspection records"))}
                <style>{styles}</style>
            </head>
            <body>
                {PublicPageChrome.Header}
                <main>
                    <a class="back-link" href="/e/{equipment.Id}">{ArrowLeftIcon}<span>Back to {Encode(equipment.Name)}</span></a>
                    <h1>{Encode(equipment.Name)}</h1>
                    <p class="subheading">{Encode(equipment.SerialNumber)} &middot; Inspection records</p>
                    {listBody}
                </main>
            </body>
            </html>
            """;
    }
}
