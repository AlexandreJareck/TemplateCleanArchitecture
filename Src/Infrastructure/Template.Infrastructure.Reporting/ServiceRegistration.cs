using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Template.Application.Interfaces.Reporting;
using Template.Infrastructure.Reporting.Pdf;
using Template.Infrastructure.Reporting.Spreadsheets;

namespace Template.Infrastructure.Reporting;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers the PDF renderer (one lazily launched headless Chromium per process) and the XLSX writer. Only the
    /// Worker composes this project; the WebApi never renders documents.
    /// </summary>
    public static IServiceCollection AddReportingInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<ReportRenderingSettings>()
            .Bind(configuration.GetSection(ReportRenderingSettings.SectionName))
            .Validate(ReportRenderingSettings.IsValid, "ReportRenderingSettings requires positive timeouts and 1-8 concurrent renders.")
            .ValidateOnStart();
        services.AddSingleton<PlaywrightPdfRenderer>();
        services.AddSingleton<IPdfRenderer>(provider => provider.GetRequiredService<PlaywrightPdfRenderer>());
        services.AddSingleton<ISpreadsheetWriter, ClosedXmlSpreadsheetWriter>();
        return services;
    }
}
