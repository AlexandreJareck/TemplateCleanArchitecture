using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System.Reflection;
using Template.Application.Behaviors;
using Template.Application.Features.Orders.EventHandlers;
using Template.Application.Interfaces;
using Template.Application.Interfaces.Messaging;
using Template.Application.Services.Access;
using Template.Application.Services.Product;
using Template.Application.Services.Time;
using Template.Application.Settings;
using Microsoft.Extensions.Options;

namespace Template.Application;
public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationLayer(this IServiceCollection services)
    {
        services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(ServiceRegistration).Assembly);
            cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.TryAddSingleton<IClock, SystemClock>();

        services.AddScoped<IProductService, ProductService>();
        services.AddScoped<IOrderPlacedEventHandler, OrderPlacedEventHandler>();

        services.AddScoped<SecurityAuditRecorder>();
        services.AddScoped<Services.MasterData.MasterDataWriter>();
        services.AddScoped<Services.Integrations.PtaxRateResolver>();
        services.AddScoped<Services.Procurement.ProcurementWriter>();
        services.AddScoped<IProcurementIntegrationEventHandler, Services.Procurement.ProcurementIntegrationEventHandler>();
        services.AddScoped<Features.Requisitions.RequisitionAccess>();
        services.AddScoped<Features.Sourcing.SourcingAccess>();
        services.AddScoped<Features.Sourcing.SourcingExchangeRates>();
        services.AddScoped<Features.Reporting.ReportAccess>();
        // Phase 30: report builders and the Worker use cases; the renderer and storage adapters come from Infrastructure.
        services.AddScoped<Services.Reporting.IReportBuilder, Services.Reporting.RequisitionPdfBuilder>();
        services.AddScoped<Services.Reporting.IReportBuilder, Services.Reporting.QuoteComparisonPdfBuilder>();
        services.AddScoped<Services.Reporting.IReportBuilder, Services.Reporting.BudgetConsumptionXlsxBuilder>();
        services.AddScoped<Services.Reporting.IReportBuilder, Services.Reporting.SupplierQuotesXlsxBuilder>();
        services.AddScoped<Services.Reporting.ReportJobProcessor>();
        services.AddScoped<Services.Reporting.ReportMaintenance>();
        // Phase 31: approval use cases; the WorkflowFunctions host runs the activities, the Worker the starter and sweep.
        services.AddScoped<Features.Approvals.ApprovalAccess>();
        services.AddScoped<Features.Approvals.ApprovalOutcomes>();
        services.AddScoped<Features.Approvals.ApprovalWorkflowActivities>();
        services.AddScoped<Features.Approvals.ApprovalReconciliation>();
        services.AddScoped<Features.Approvals.ApprovalWorkflowStarter>();
        services.AddScoped<Features.Approvals.ApprovalCascade>();
        // Phase 32: purchase orders and receiving; the Worker runs the purchasing consumer and the order document builder.
        services.AddScoped<Features.Purchasing.PurchasingAccess>();
        services.AddScoped<Features.Purchasing.PurchasingEventHandler>();
        services.AddScoped<Services.Reporting.IReportBuilder, Services.Reporting.PurchaseOrderPdfBuilder>();
        services.AddScoped<ISessionRevocationService, SessionRevocationService>();
        services.AddSingleton<IValidateOptions<SessionSettings>, SessionSettingsValidator>();
        // The composition root binds SessionSettings from configuration and validates it on start.

        return services;
    }
}
