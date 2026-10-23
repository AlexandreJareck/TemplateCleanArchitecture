#nullable enable
using System.Globalization;
using Template.Application.Features.Reporting;
using Template.Application.Reporting;
using Template.Domain.Files;
using Template.Domain.Reporting;

namespace Template.Application.Services.Reporting;

/// <summary>Everything a builder may depend on: the job snapshot, the generation instant and the organization time zone.</summary>
public sealed record ReportBuildContext(ReportJob Job, ReportParameters Parameters, DateTime GeneratedAtUtc, TimeZoneInfo TimeZone, ReportPolicy Policy)
{
    public DateTime AsOfUtc => Job.AsOfUtc;
}

/// <summary>Built content: exactly one of <see cref="Pdf"/> or <see cref="Workbook"/>, plus the sanitized download name.</summary>
public sealed record ReportContent(string FileName, ReportDocument? Pdf = null, WorkbookDocument? Workbook = null);

/// <summary>Builds the provider-neutral content of one catalog entry from server-owned sources (RPT-003).</summary>
public interface IReportBuilder
{
    ReportType Type { get; }

    Task<ReportContent> BuildAsync(ReportBuildContext context, CancellationToken cancellationToken);
}

/// <summary>Permanent source failure (missing or changed source, oversized result) with a catalogued safe code.</summary>
public sealed class ReportSourceException(string failureCode, string message) : Exception(message)
{
    public string FailureCode { get; } = ReportFailureCodes.All.Contains(failureCode)
        ? failureCode
        : throw new ArgumentException("Unknown failure code.", nameof(failureCode));
}

/// <summary>
/// Organization-locale (pt-BR) formatting shared by every report (RPT-004): money with its ISO currency, local dates
/// and instants converted to the organization time zone. Amounts are formatted, never recomputed.
/// </summary>
public static class ReportText
{
    public const string Language = "pt-BR";

    public static CultureInfo Culture { get; } = CultureInfo.GetCultureInfo(Language);

    public static string Money(decimal amount, string currency) =>
        $"{amount.ToString("#,##0.00", Culture)} {currency}";

    public static string Decimal(decimal value, int maxDecimals = 3) =>
        value.ToString("#,##0." + new string('#', Math.Max(maxDecimals, 0)), Culture).TrimEnd(',');

    public static string Rate(decimal value) => value.ToString("0.000000", Culture);

    public static string Date(DateOnly value) => value.ToString("dd/MM/yyyy", Culture);

    public static DateTime Local(DateTime utc, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), zone);

    public static string Instant(DateTime utc, TimeZoneInfo zone) =>
        Local(utc, zone).ToString("dd/MM/yyyy HH:mm:ss", Culture);

    public static string Integer(long value) => value.ToString("#,##0", Culture);

    public static string YesNo(bool value) => value ? "Sim" : "Não";

    /// <summary>RPT-004 context common to every report; <paramref name="extra"/> adds filters, currency and policy versions.</summary>
    public static IReadOnlyList<ReportField> Context(ReportBuildContext context, params ReportField[] extra) =>
    [
        new("Gerado em", $"{Instant(context.GeneratedAtUtc, context.TimeZone)} ({context.TimeZone.Id})"),
        new("Posição em (as-of)", $"{Instant(context.AsOfUtc, context.TimeZone)} ({context.TimeZone.Id})"),
        new("Fuso horário", context.TimeZone.Id),
        .. extra,
        new("Política de relatórios", $"ReportPolicy v{context.Job.PolicyVersion}"),
        new("Modelo", context.Job.TemplateVersion),
        new("Job", context.Job.Id.ToString("D"))
    ];

    /// <summary>Safe download name: ASCII letters, digits, dot, dash and underscore, with the catalog extension.</summary>
    public static string FileName(ReportType type, params string[] parts)
    {
        var raw = string.Join("-", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        var ascii = new string(raw.Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            .Select(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_' or '.' ? character : '-')
            .ToArray()).Trim('-', '.');
        var name = (ascii.Length == 0 ? "relatorio" : ascii.Length > 100 ? ascii[..100] : ascii) + ReportCatalog.Extension(type);
        return StoredFile.SanitizeName(name) ?? "relatorio" + ReportCatalog.Extension(type);
    }

    public static string Status(string value) => value switch
    {
        "Draft" => "Rascunho",
        "Submitted" => "Submetida",
        "InSourcing" => "Em cotação",
        "PendingApproval" => "Aguardando aprovação",
        "Approved" => "Aprovada",
        "Ordered" => "Pedido emitido",
        "Closed" => "Encerrada",
        "Rejected" => "Rejeitada",
        "Cancelled" => "Cancelada",
        "Expired" => "Expirada",
        "Published" => "Publicada",
        "AwardPending" => "Adjudicação pendente",
        "Awarded" => "Adjudicada",
        "Reserved" => "Reservado",
        "Committed" => "Comprometido",
        "Released" => "Liberado",
        "Adjusted" => "Ajustado",
        "Active" => "Ativa",
        "Current" => "Vigente",
        "Superseded" => "Substituída",
        "Withdrawn" => "Retirada",
        "Complete" => "Completa",
        "Incomplete" => "Incompleta",
        "Open" => "Aberto",
        "Planned" => "Planejado",
        _ => value
    };
}
