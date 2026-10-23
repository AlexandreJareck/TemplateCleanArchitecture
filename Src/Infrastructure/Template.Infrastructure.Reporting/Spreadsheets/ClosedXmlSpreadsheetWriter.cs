using System.Text;
using ClosedXML.Excel;
using Template.Application.Interfaces.Reporting;
using Template.Application.Reporting;

namespace Template.Infrastructure.Reporting.Spreadsheets;

/// <summary>
/// <see cref="ISpreadsheetWriter"/> on ClosedXML (D23-6). Each sheet has its RPT-004 context block, a bold frozen header
/// row with an auto filter, typed cells with explicit number formats and an optional totals row of server-computed
/// values. No formula is ever written; text is written as a string value and any text starting with <c>=</c>,
/// <c>+</c>, <c>-</c>, <c>@</c>, tab or carriage return gets Excel's quote prefix (the apostrophe equivalent), so
/// spreadsheet applications never evaluate it (RPT-007). XML-invalid control characters are removed.
/// </summary>
public sealed class ClosedXmlSpreadsheetWriter : ISpreadsheetWriter
{
    public const string MoneyFormat = "#,##0.00";
    public const string DecimalFormat = "#,##0.###";
    public const string RateFormat = "0.000000";
    public const string PercentFormat = "0.00%";
    public const string IntegerFormat = "#,##0";
    public const string DateFormat = "dd/mm/yyyy";
    public const string DateTimeFormat = "dd/mm/yyyy hh:mm:ss";
    public const int MaxCellText = 32_767;

    private static readonly XLColor HeaderFill = XLColor.FromHtml("#1F4E79");
    private static readonly XLColor TotalsFill = XLColor.FromHtml("#EEF2F6");

    public Task WriteAsync(WorkbookDocument workbook, Stream output, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(workbook);
        ArgumentNullException.ThrowIfNull(output);
        return Task.Run(() => Write(workbook, output, cancellationToken), cancellationToken);
    }

    /// <summary>Whether a spreadsheet application could interpret <paramref name="text"/> as a formula or command (RPT-007).</summary>
    public static bool NeedsQuotePrefix(string text) => text.Length > 0 && text[0] is '=' or '+' or '-' or '@' or '\t' or '\r';

    /// <summary>Removes characters XML 1.0 cannot carry (keeps tab, line feed and carriage return) and bounds the length.</summary>
    public static string CleanText(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;
        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character is '\t' or '\n' or '\r' || (character >= 0x20 && character != '￾' && character != '￿'))
                builder.Append(character);
        }

        return builder.Length > MaxCellText ? builder.ToString(0, MaxCellText) : builder.ToString();
    }

    private static void Write(WorkbookDocument document, Stream output, CancellationToken cancellationToken)
    {
        using var workbook = new XLWorkbook();
        workbook.Properties.Title = CleanText(document.Title);
        workbook.Properties.Subject = CleanText(document.TemplateVersion);
        workbook.Properties.Author = "Template Procurement";
        workbook.Properties.Company = string.Empty;

        foreach (var sheet in document.Sheets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            WriteSheet(workbook.Worksheets.Add(sheet.Validate().Name), document, sheet, cancellationToken);
        }

        workbook.SaveAs(output, new SaveOptions { ValidatePackage = true, EvaluateFormulasBeforeSaving = false });
    }

    private static void WriteSheet(IXLWorksheet worksheet, WorkbookDocument document, WorkbookSheet sheet, CancellationToken cancellationToken)
    {
        var row = 1;
        SetText(worksheet.Cell(row, 1), document.Title).Style.Font.SetBold().Font.SetFontSize(14);
        row++;
        foreach (var field in sheet.Context)
        {
            SetText(worksheet.Cell(row, 1), field.Label).Style.Font.SetBold();
            SetText(worksheet.Cell(row, 2), field.Value);
            row++;
        }

        row++;
        var headerRow = row;
        for (var column = 0; column < sheet.Columns.Count; column++)
        {
            var cell = SetText(worksheet.Cell(headerRow, column + 1), sheet.Columns[column].Header);
            cell.Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(HeaderFill)
                .Alignment.SetWrapText(true).Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            worksheet.Column(column + 1).Width = Math.Clamp(sheet.Columns[column].Width, 6, 80);
        }

        foreach (var data in sheet.Rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            row++;
            for (var column = 0; column < data.Count; column++)
                SetValue(worksheet.Cell(row, column + 1), data[column]);
        }

        var lastDataRow = row;
        if (sheet.Totals is { } totals)
        {
            row++;
            for (var column = 0; column < totals.Count; column++)
            {
                var cell = SetValue(worksheet.Cell(row, column + 1), totals[column]);
                cell.Style.Font.SetBold().Fill.SetBackgroundColor(TotalsFill).Border.SetTopBorder(XLBorderStyleValues.Medium);
            }
        }

        worksheet.SheetView.FreezeRows(headerRow);
        worksheet.Range(headerRow, 1, Math.Max(lastDataRow, headerRow), sheet.Columns.Count).SetAutoFilter();
        worksheet.PageSetup.PageOrientation = XLPageOrientation.Landscape;
        worksheet.PageSetup.FitToPages(1, 0);
        worksheet.PageSetup.SetRowsToRepeatAtTop(headerRow, headerRow);
    }

    private static IXLCell SetValue(IXLCell cell, WorkbookCell value)
    {
        switch (value.Kind)
        {
            case WorkbookCellKind.Text:
                return SetText(cell, value.Text);
            case WorkbookCellKind.Date when value.Date is { } date:
                cell.SetValue(date.ToDateTime(TimeOnly.MinValue));
                cell.Style.NumberFormat.Format = DateFormat;
                return cell;
            case WorkbookCellKind.DateTime when value.Instant is { } instant:
                cell.SetValue(instant);
                cell.Style.NumberFormat.Format = DateTimeFormat;
                return cell;
            case WorkbookCellKind.Integer or WorkbookCellKind.Decimal or WorkbookCellKind.Money or WorkbookCellKind.Rate or WorkbookCellKind.Percent
                when value.Number is { } number:
                cell.SetValue(number);
                cell.Style.NumberFormat.Format = value.Kind switch
                {
                    WorkbookCellKind.Integer => IntegerFormat,
                    WorkbookCellKind.Money => MoneyFormat,
                    WorkbookCellKind.Rate => RateFormat,
                    WorkbookCellKind.Percent => PercentFormat,
                    _ => DecimalFormat
                };
                cell.Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                return cell;
            default:
                return SetText(cell, string.Empty);
        }
    }

    private static IXLCell SetText(IXLCell cell, string? text)
    {
        var clean = CleanText(text);
        cell.SetValue(clean);
        cell.Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
        if (NeedsQuotePrefix(clean))
            cell.Style.IncludeQuotePrefix = true;
        return cell;
    }
}
