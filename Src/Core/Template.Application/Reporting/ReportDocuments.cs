#nullable enable
namespace Template.Application.Reporting;

/// <summary>
/// Provider-neutral content of a PDF report. Builders produce only text values; the renderer adapter encodes every
/// value into its versioned server template (RPT-008), so no caller can inject markup or scripts.
/// </summary>
/// <param name="Title">Document title.</param>
/// <param name="Subtitle">Business identifier shown under the title (for example the requisition number).</param>
/// <param name="Language">BCP 47 language of the labels (the organization locale).</param>
/// <param name="Context">RPT-004 context: generation and as-of instants, time zone, filters, currency, policy versions.</param>
/// <param name="Sections">Body sections in order.</param>
/// <param name="TemplateVersion">Server template version pinned on the job.</param>
/// <param name="Footer">Footer text repeated on every page next to the page number.</param>
public sealed record ReportDocument(
    string Title,
    string Subtitle,
    string Language,
    IReadOnlyList<ReportField> Context,
    IReadOnlyList<ReportSection> Sections,
    string TemplateVersion,
    string Footer);

public sealed record ReportField(string Label, string Value);

public enum ReportColumnAlignment
{
    Start,
    End,
    Center
}

public sealed record ReportColumn(string Header, ReportColumnAlignment Alignment = ReportColumnAlignment.Start);

/// <summary>A table with text cells; <paramref name="Totals"/> is an optional emphasized last row.</summary>
public sealed record ReportTable(IReadOnlyList<ReportColumn> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, IReadOnlyList<string>? Totals = null)
{
    public ReportTable Validate()
    {
        if (Columns.Count == 0 || Rows.Any(row => row.Count != Columns.Count) || (Totals is not null && Totals.Count != Columns.Count))
            throw new ArgumentException("Every table row must have one cell per column.");
        return this;
    }
}

/// <summary>A heading with optional key/value fields, a table, an empty-state message and notes.</summary>
public sealed record ReportSection(
    string Heading,
    IReadOnlyList<ReportField>? Fields = null,
    ReportTable? Table = null,
    string? EmptyMessage = null,
    IReadOnlyList<string>? Notes = null);

/// <summary>Kind of a typed workbook cell (RPT-007: user text is always written as text, never as a formula).</summary>
public enum WorkbookCellKind
{
    Text,
    Integer,
    Decimal,
    Money,
    Rate,
    Percent,
    Date,
    DateTime
}

/// <summary>
/// One typed spreadsheet cell. <see cref="UserControlled"/> marks text that came from users or suppliers; the writer
/// neutralizes formula prefixes in it (RPT-007). Numbers are written as numbers with a display format.
/// </summary>
public sealed record WorkbookCell(WorkbookCellKind Kind, string? Text = null, decimal? Number = null, DateTime? Instant = null, DateOnly? Date = null, bool UserControlled = false)
{
    public static WorkbookCell Label(string text) => new(WorkbookCellKind.Text, text);

    public static WorkbookCell UserText(string? text) => new(WorkbookCellKind.Text, text ?? string.Empty, UserControlled: true);

    public static WorkbookCell Integer(long value) => new(WorkbookCellKind.Integer, Number: value);

    public static WorkbookCell Decimal(decimal value) => new(WorkbookCellKind.Decimal, Number: value);

    public static WorkbookCell Money(decimal value) => new(WorkbookCellKind.Money, Number: value);

    public static WorkbookCell Rate(decimal value) => new(WorkbookCellKind.Rate, Number: value);

    public static WorkbookCell Percent(decimal value) => new(WorkbookCellKind.Percent, Number: value);

    public static WorkbookCell DateValue(DateOnly value) => new(WorkbookCellKind.Date, Date: value);

    /// <summary>An instant already converted to the organization local time (the column header names the time zone).</summary>
    public static WorkbookCell LocalDateTime(DateTime localValue) => new(WorkbookCellKind.DateTime, Instant: DateTime.SpecifyKind(localValue, DateTimeKind.Unspecified));

    public static WorkbookCell Empty { get; } = new(WorkbookCellKind.Text, string.Empty);
}

public sealed record WorkbookColumn(string Header, double Width = 18);

/// <summary>
/// A worksheet: RPT-004 context rows above a header row and typed data rows (frozen header, auto filter). Totals are
/// server-computed values from the same rules as the transactional use case, never spreadsheet formulas.
/// </summary>
public sealed record WorkbookSheet(
    string Name,
    IReadOnlyList<ReportField> Context,
    IReadOnlyList<WorkbookColumn> Columns,
    IReadOnlyList<IReadOnlyList<WorkbookCell>> Rows,
    IReadOnlyList<WorkbookCell>? Totals = null)
{
    public const int NameMaxLength = 31;

    public WorkbookSheet Validate()
    {
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > NameMaxLength || Name.IndexOfAny(['[', ']', ':', '*', '?', '/', '\\']) >= 0)
            throw new ArgumentException("Invalid worksheet name.");
        if (Columns.Count == 0 || Rows.Any(row => row.Count != Columns.Count) || (Totals is not null && Totals.Count != Columns.Count))
            throw new ArgumentException("Every worksheet row must have one cell per column.");
        return this;
    }
}

/// <summary>Workbook content plus document properties (title, template version).</summary>
public sealed record WorkbookDocument(string Title, string Language, string TemplateVersion, IReadOnlyList<WorkbookSheet> Sheets);
