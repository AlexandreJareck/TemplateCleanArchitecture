using System.Net;
using System.Reflection;
using System.Text;
using Template.Application.Reporting;

namespace Template.Infrastructure.Reporting.Pdf;

/// <summary>
/// Versioned server template <c>report-document.v1</c> (RPT-008): a fixed HTML skeleton and stylesheet in which every
/// value of a <see cref="ReportDocument"/> is HTML-encoded. There is no template language, script or user-supplied
/// markup; a restrictive Content-Security-Policy blocks every network fetch and script, and the fonts are embedded as
/// data URIs so the output does not depend on fonts installed on the host.
/// </summary>
public static class ReportHtmlTemplate
{
    public const string Version = "report-document.v1";
    public const string FontFamily = "Report Sans";
    public const int WideTableColumns = 9;

    private static readonly Lazy<string> Stylesheet = new(() => ReadText("Template.Reporting.report-document.v1.css"));
    private static readonly Lazy<string> RegularFace = new(() => Face(400, "Template.Reporting.Fonts.Roboto-Regular.ttf"));
    private static readonly Lazy<string> FontFaces = new(() =>
        Face(400, "Template.Reporting.Fonts.Roboto-Regular.ttf") + Face(700, "Template.Reporting.Fonts.Roboto-Bold.ttf"));

    public static string Render(ReportDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        var html = new StringBuilder(16 * 1024);
        html.Append("<!doctype html><html lang=\"").Append(Encode(document.Language)).Append("\"><head><meta charset=\"utf-8\">")
            .Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'; font-src data:; img-src data:\">")
            .Append("<meta name=\"generator\" content=\"").Append(Version).Append(' ').Append(Encode(document.TemplateVersion)).Append("\">")
            .Append("<title>").Append(Encode(document.Title)).Append(" — ").Append(Encode(document.Subtitle)).Append("</title>")
            .Append("<style>").Append(FontFaces.Value).Append(Stylesheet.Value).Append("</style></head><body>");

        html.Append("<header class=\"report-header\"><h1>").Append(Encode(document.Title)).Append("</h1><p class=\"subtitle\">")
            .Append(Encode(document.Subtitle)).Append("</p></header>");
        html.Append("<section class=\"context\" aria-label=\"Contexto do relatório\">");
        AppendFields(html, document.Context);
        html.Append("</section>");

        foreach (var section in document.Sections)
        {
            html.Append("<section><h2>").Append(Encode(section.Heading)).Append("</h2>");
            if (section.Fields is { Count: > 0 } fields)
                AppendFields(html, fields);
            if (section.Table is { } table && table.Rows.Count > 0)
                AppendTable(html, table.Validate());
            else if (section.Fields is not { Count: > 0 } && section.EmptyMessage is { } empty)
                html.Append("<p class=\"empty\">").Append(Encode(empty)).Append("</p>");
            if (section.Notes is { Count: > 0 } notes)
            {
                html.Append("<ul class=\"notes\">");
                foreach (var note in notes)
                    html.Append("<li>").Append(Encode(note)).Append("</li>");
                html.Append("</ul>");
            }

            html.Append("</section>");
        }

        return html.Append("</body></html>").ToString();
    }

    /// <summary>
    /// Footer repeated on every page: the encoded footer text and Chromium's page counters, in the same embedded font
    /// (header and footer templates are separate documents, so the regular face is declared again).
    /// </summary>
    public static string Footer(ReportDocument document) =>
        $"<style>{RegularFace.Value}</style>"
        + $"<div style=\"font-family:'{FontFamily}';font-size:7.5pt;color:#52606d;width:100%;padding:0 12mm;display:flex;justify-content:space-between\">"
        + $"<span>{Encode(document.Footer)}</span><span>Página <span class=\"pageNumber\"></span> de <span class=\"totalPages\"></span></span></div>";

    public static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    private static void AppendFields(StringBuilder html, IReadOnlyList<ReportField> fields)
    {
        html.Append("<dl>");
        foreach (var field in fields)
            html.Append("<dt>").Append(Encode(field.Label)).Append("</dt><dd>").Append(Encode(field.Value)).Append("</dd>");
        html.Append("</dl>");
    }

    private static void AppendTable(StringBuilder html, ReportTable table)
    {
        // Tables with many columns use the compact style so amounts and dates never wrap inside a cell.
        html.Append(table.Columns.Count >= WideTableColumns ? "<table class=\"wide\"><thead><tr>" : "<table><thead><tr>");
        foreach (var column in table.Columns)
            html.Append("<th scope=\"col\"").Append(AlignmentClass(column.Alignment)).Append('>').Append(Encode(column.Header)).Append("</th>");
        html.Append("</tr></thead><tbody>");
        foreach (var row in table.Rows)
            AppendRow(html, table, row);
        html.Append("</tbody>");
        if (table.Totals is { } totals)
        {
            html.Append("<tfoot>");
            AppendRow(html, table, totals);
            html.Append("</tfoot>");
        }

        html.Append("</table>");
    }

    private static void AppendRow(StringBuilder html, ReportTable table, IReadOnlyList<string> cells)
    {
        html.Append("<tr>");
        for (var index = 0; index < cells.Count; index++)
            html.Append("<td").Append(AlignmentClass(table.Columns[index].Alignment)).Append('>').Append(Encode(cells[index])).Append("</td>");
        html.Append("</tr>");
    }

    private static string AlignmentClass(ReportColumnAlignment alignment) => alignment switch
    {
        ReportColumnAlignment.End => " class=\"end\"",
        ReportColumnAlignment.Center => " class=\"center\"",
        _ => string.Empty
    };

    private static string Face(int weight, string resource) =>
        $"@font-face{{font-family:\"{FontFamily}\";font-weight:{weight};font-style:normal;src:url(data:font/ttf;base64,{Convert.ToBase64String(ReadBytes(resource))}) format(\"truetype\");}}";

    private static byte[] ReadBytes(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing report asset {resource}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }

    private static string ReadText(string resource) => Encoding.UTF8.GetString(ReadBytes(resource));
}
