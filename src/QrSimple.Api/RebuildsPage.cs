using System.Globalization;
using System.Net;

namespace QrSimple.Api;

public static class RebuildsPage
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

    // Every record renders, newest first — no recent/older split. Rebuilds are years apart, so
    // the 6-month recency window this page inherited from periodic inspections would have hidden
    // almost the whole history behind a collapsed drawer.
    public static string Render(Equipment equipment, IReadOnlyList<RebuildListItem> rebuilds)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);

        string Row(RebuildListItem item)
        {
            // The PDF is optional (a record is its date and note), so this link exists only when
            // one was actually attached — never render a link to a 404.
            var pdfLink = item.HasFile
                ? $"""
                    <a class="open-pdf" href="/rebuilds/{item.Id}/content" target="_blank" rel="noopener noreferrer">
                        {FileTextIcon}<span>Open PDF</span>{ExternalLinkIcon}
                    </a>
                    """
                : "";

            return $"""
                <article class="rebuild-row">
                    <div class="rebuild-row-top">
                        <span class="rebuild-date">{item.RebuildDate.ToString("d MMM yyyy", CultureInfo.InvariantCulture)}</span>
                    </div>
                    <p class="rebuild-note">{Encode(item.Note)}</p>
                    {pdfLink}
                </article>
                """;
        }

        var listBody = rebuilds.Count == 0
            ? """<p class="empty-documents">No rebuild records yet.</p>"""
            : $"""<div class="rebuild-list">{string.Concat(rebuilds.Select(Row))}</div>""";

        // Head, header, and the shared :root/body/.icon/.site-header/main/.panel rules live in
        // PublicPageChrome, alongside ScanPage.cs — never introduce a literal colour on this
        // surface, reuse the var(--brand-*) tokens from tokens.css.
        var styles = PublicPageChrome.BaseStyles + """
            .back-link { display: inline-flex; align-items: center; gap: 8px; margin-bottom: 14px; color: var(--brand-navy); font-weight: 600; text-decoration: none; }
            .back-link:hover { text-decoration: underline; }
            .back-link:focus-visible { outline: 3px solid var(--brand-navy); outline-offset: 3px; }
            h1 { margin: 0; font-size: clamp(1.4rem, 5vw, 2rem); line-height: 1.2; font-weight: 700; }
            .subheading { margin: 6px 0 20px; color: var(--brand-muted); font-weight: 600; }
            .rebuild-list { display: grid; gap: 12px; margin: 0 0 18px; }
            .rebuild-row { padding: 16px 18px; border-radius: 14px; background: var(--brand-surface); border: 1px solid var(--brand-border); box-shadow: var(--brand-shadow); }
            .rebuild-row-top { display: flex; align-items: center; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
            .rebuild-date { font-size: 1.15rem; font-weight: 700; color: var(--brand-text); }
            .rebuild-note { margin: 10px 0 0; color: var(--brand-text); overflow-wrap: anywhere; }
            .open-pdf { display: inline-flex; align-items: center; gap: 8px; margin-top: 12px; color: var(--brand-primary); font-weight: 700; text-decoration: none; }
            .open-pdf:hover { text-decoration: underline; }
            .open-pdf:focus-visible { outline: 3px solid var(--brand-navy); outline-offset: 3px; }
            .empty-documents { margin: 0; padding: 18px; border-radius: 12px; background: var(--brand-surface); border: 1px solid var(--brand-border); color: var(--brand-muted); text-align: center; }
            @media (max-width: 480px) {
                .rebuild-row { padding: 12px 14px; }
                .rebuild-date { font-size: 1.05rem; }
            }
            """;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
                {PublicPageChrome.HeadTags(Encode($"{equipment.Name} — Rebuild history"))}
                <style>{styles}</style>
            </head>
            <body>
                {PublicPageChrome.Header}
                <main>
                    <a class="back-link" href="/e/{equipment.Id}">{ArrowLeftIcon}<span>Back to {Encode(equipment.Name)}</span></a>
                    <h1>{Encode(equipment.Name)}</h1>
                    <p class="subheading">{Encode(equipment.SerialNumber)} &middot; Rebuild history</p>
                    {listBody}
                </main>
            </body>
            </html>
            """;
    }
}
