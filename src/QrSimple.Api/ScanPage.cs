using System.Net;

namespace QrSimple.Api;

public static class ScanPage
{
    public static string Render(Equipment equipment, IReadOnlyList<Document> documents)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);

        var photo = documents.FirstOrDefault(document =>
            document.Label.Equals("Equipment Photo", StringComparison.OrdinalIgnoreCase) ||
            document.Label.Equals("Equipment Image", StringComparison.OrdinalIgnoreCase));

        var retiredNotice = equipment.Status == EquipmentStatus.Retired
            ? """<div class="retired" role="status">This equipment is no longer in service.</div>"""
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

        var documentLinks = string.Concat(documents
            .Where(document => document != photo)
            .OrderBy(document => document.Label.Equals("User manual", StringComparison.OrdinalIgnoreCase) ? 0
                : document.Label.Equals("Maintenance instruction", StringComparison.OrdinalIgnoreCase) ? 1
                : 2)
            .ThenBy(document => document.Label)
            .Select(document =>
            {
                var href = document.Content is not null ? $"/documents/{document.Id}/content" : document.Url;
                return $"""
                    <a class="panel document" href="{Encode(href ?? "")}" target="_blank" rel="noopener noreferrer">
                        <span>{Encode(document.Label)}</span><span class="arrow" aria-hidden="true">↗</span>
                    </a>
                    """;
            }));

        var documentsSection = documentLinks.Length > 0
            ? $"""<nav class="documents" aria-label="Equipment documents">{documentLinks}</nav>"""
            : """<p class="empty-documents">No documents are available for this equipment.</p>""";

        const string styles = """
            :root { color-scheme: light; font-family: Inter, ui-sans-serif, system-ui, -apple-system, BlinkMacSystemFont, "Segoe UI", sans-serif; }
            * { box-sizing: border-box; }
            body { margin: 0; background: #eef3fa; color: #14213d; }
            main { width: min(100%, 760px); min-height: 100vh; margin: 0 auto; padding: clamp(12px, 3vw, 24px); }
            .stack { display: grid; gap: 14px; }
            .photo { min-height: 220px; display: grid; place-items: center; padding: 18px; background: #356fbd; border-radius: 18px; box-shadow: 0 8px 24px rgb(28 68 123 / 18%); }
            .photo-frame { width: min(100%, 340px); aspect-ratio: 16 / 10; display: grid; place-items: center; overflow: hidden; background: white; border-radius: 12px; }
            .photo img { width: 100%; height: 100%; object-fit: contain; }
            .placeholder { width: 74%; max-height: 78%; color: #356fbd; }
            .panel { min-height: 84px; display: flex; align-items: center; justify-content: center; padding: 20px; border: 0; border-radius: 14px; background: #356fbd; color: white; text-align: center; box-shadow: 0 6px 18px rgb(28 68 123 / 15%); }
            h1 { margin: 0; font-size: clamp(1.55rem, 6vw, 2.25rem); line-height: 1.15; font-weight: 700; }
            .category { font-size: clamp(1.25rem, 5vw, 1.75rem); font-weight: 600; }
            .documents { display: grid; gap: 14px; }
            .document { position: relative; padding-inline: 52px; font-size: clamp(1.2rem, 5vw, 1.65rem); font-weight: 600; text-decoration: none; transition: transform .15s ease, background .15s ease; }
            .document:hover { background: #285da3; transform: translateY(-1px); }
            .document:focus-visible { outline: 4px solid #f5b700; outline-offset: 3px; }
            .arrow { position: absolute; right: 24px; font-size: 1.2em; }
            .retired { padding: 16px 20px; border: 2px solid #b42318; border-radius: 12px; background: #fff1f0; color: #8a1c13; text-align: center; font-weight: 700; }
            .details { margin-top: 22px; padding: 22px; border-radius: 14px; background: white; box-shadow: 0 6px 18px rgb(28 68 123 / 9%); }
            .details h2 { margin: 0 0 14px; font-size: 1.15rem; }
            dl { display: grid; grid-template-columns: max-content 1fr; gap: 12px 18px; margin: 0; }
            dt { color: #516178; font-weight: 600; }
            dd { margin: 0; overflow-wrap: anywhere; }
            .empty-documents { margin: 0; padding: 18px; border-radius: 12px; background: white; color: #516178; text-align: center; }
            @media (max-width: 480px) {
                main { padding: 10px; }
                .stack, .documents { gap: 10px; }
                .photo { min-height: 190px; border-radius: 12px; }
                .panel { min-height: 76px; border-radius: 10px; }
                dl { grid-template-columns: 1fr; gap: 4px; }
                dd + dt { margin-top: 10px; }
            }
            """;

        return $"""
            <!doctype html>
            <html lang="en">
            <head>
                <meta charset="utf-8">
                <meta name="viewport" content="width=device-width, initial-scale=1">
                <title>{Encode(equipment.Name)}</title>
                <style>{styles}</style>
            </head>
            <body>
                <main>
                    <div class="stack">
                        <section class="photo" aria-label="Equipment photo">
                            <div class="photo-frame">{equipmentImage}</div>
                        </section>
                        <section class="panel"><h1>{Encode(equipment.Name)}</h1></section>
                        <section class="panel category">{Encode(equipment.Category)}</section>
                        {retiredNotice}
                        {documentsSection}
                    </div>
                    <section class="details" aria-labelledby="details-heading">
                        <h2 id="details-heading">Equipment details</h2>
                        <dl>
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
