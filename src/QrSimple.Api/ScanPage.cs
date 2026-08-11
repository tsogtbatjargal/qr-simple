using System.Net;

namespace QrSimple.Api;

public static class ScanPage
{
    public static string Render(Equipment equipment, IReadOnlyList<Document> documents)
    {
        string Encode(string value) => WebUtility.HtmlEncode(value);

        var retiredNotice = equipment.Status == "Retired"
            ? "<p><strong>This equipment is no longer in service.</strong></p>"
            : "";

        var documentLinks = string.Concat(documents.Select(d =>
            $"""<p><a href="{Encode(d.Url)}">{Encode(d.Label)}</a></p>"""));

        return $"""
            <!doctype html>
            <html>
            <head><meta charset="utf-8"><title>{Encode(equipment.Name)}</title></head>
            <body>
                <h1>{Encode(equipment.Name)}</h1>
                {retiredNotice}
                <p>Category: {Encode(equipment.Category)}</p>
                <p>Serial/Asset Number: {Encode(equipment.SerialNumber)}</p>
                <p>Site: {Encode(equipment.Site)}</p>
                {documentLinks}
            </body>
            </html>
            """;
    }
}
