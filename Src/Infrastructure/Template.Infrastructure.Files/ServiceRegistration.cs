using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.Files;

namespace Template.Infrastructure.Files;

public static class ServiceRegistration
{
    /// <summary>
    /// Registers <see cref="IFileStorage"/>, <see cref="IFileStorageProbe"/> and the no-op <see cref="IMalwareScanner"/>
    /// (FIL-006). Test uses <see cref="InMemoryFileStorage"/>; otherwise <c>FileStorageSettings:Enabled</c> selects the
    /// blob adapter (which requires <c>ConnectionStrings:BlobStorage</c>, validated at start) or
    /// <see cref="UnavailableFileStorage"/>.
    /// </summary>
    public static IServiceCollection AddFilesInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useInMemoryStorage)
    {
        var section = configuration.GetSection(FileStorageSettings.SectionName);
        var enabled = section.GetValue<bool>(nameof(FileStorageSettings.Enabled));
        var connectionString = configuration.GetConnectionString(FileStorageSettings.ConnectionStringName);
        services.AddOptions<FileStorageSettings>()
            .Bind(section)
            .Validate(FileStorageSettings.IsValid, "FileStorageSettings requires a valid private container name, a positive timeout and 0-5 retries.")
            .Validate(settings => useInMemoryStorage || !settings.Enabled || !string.IsNullOrWhiteSpace(connectionString),
                $"ConnectionStrings:{FileStorageSettings.ConnectionStringName} is required when FileStorageSettings:Enabled is true.")
            .ValidateOnStart();
        services.AddSingleton<IMalwareScanner, NoOpMalwareScanner>();

        if (useInMemoryStorage)
            services.AddSingleton<InMemoryFileStorage>();
        else if (enabled)
            services.AddSingleton(provider => new BlobFileStorage(
                connectionString!, provider.GetRequiredService<IOptions<FileStorageSettings>>(), provider.GetRequiredService<ILogger<BlobFileStorage>>()));
        else
            services.AddSingleton<UnavailableFileStorage>();

        services.AddSingleton<IFileStorage>(provider => useInMemoryStorage
            ? provider.GetRequiredService<InMemoryFileStorage>()
            : enabled ? provider.GetRequiredService<BlobFileStorage>() : provider.GetRequiredService<UnavailableFileStorage>());
        services.AddSingleton<IFileStorageProbe>(provider => (IFileStorageProbe)provider.GetRequiredService<IFileStorage>());
        return services;
    }
}
