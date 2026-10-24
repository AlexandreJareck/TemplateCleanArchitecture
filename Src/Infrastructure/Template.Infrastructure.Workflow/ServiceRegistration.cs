using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Approvals;

namespace Template.Infrastructure.Workflow;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers <see cref="IApprovalWorkflowClient"/> (Phase 31, ADR-0003 §5). <paramref name="useInMemoryClient"/>
    /// (Test environment) selects <see cref="InMemoryApprovalWorkflowClient"/>; otherwise the HTTP client when
    /// <c>ApprovalWorkflowSettings:Enabled</c> is true, and <see cref="UnavailableApprovalWorkflowClient"/> when it is
    /// false. Building the container never contacts the Functions host. Default request logging is removed so URLs and
    /// headers never reach the logs. The in-memory client talks to no host, so its settings are bound but not validated
    /// (a Test host needs no internal key).
    /// </summary>
    public static IServiceCollection AddWorkflowInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useInMemoryClient)
    {
        var options = services.AddOptions<ApprovalWorkflowSettings>().Bind(configuration.GetSection(ApprovalWorkflowSettings.SectionName));

        if (useInMemoryClient)
        {
            services.AddSingleton<InMemoryApprovalWorkflowClient>();
            services.AddSingleton<IApprovalWorkflowClient>(provider => provider.GetRequiredService<InMemoryApprovalWorkflowClient>());
            return services;
        }

        services.AddSingleton<IValidateOptions<ApprovalWorkflowSettings>, ApprovalWorkflowSettingsValidator>();
        options.ValidateOnStart();

        services.AddSingleton<UnavailableApprovalWorkflowClient>();
        services.AddHttpClient<HttpApprovalWorkflowClient>((provider, client) =>
        {
            var settings = provider.GetRequiredService<IOptions<ApprovalWorkflowSettings>>().Value;
            if (settings.Enabled)
                client.BaseAddress = new Uri(settings.BaseUrl, UriKind.Absolute);
            // The per-call timeout is enforced by the client; this only caps the worst case.
            client.Timeout = settings.Timeout + TimeSpan.FromSeconds(5);
            client.MaxResponseContentBufferSize = 64 * 1024;
        }).RemoveAllLoggers();
        services.AddTransient<IApprovalWorkflowClient>(provider =>
            provider.GetRequiredService<IOptions<ApprovalWorkflowSettings>>().Value.Enabled
                ? provider.GetRequiredService<HttpApprovalWorkflowClient>()
                : provider.GetRequiredService<UnavailableApprovalWorkflowClient>());
        return services;
    }
}
