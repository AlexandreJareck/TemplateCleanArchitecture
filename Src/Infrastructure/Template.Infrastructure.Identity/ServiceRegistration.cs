using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Template.Application.Interfaces.UserInterfaces;
using Template.Infrastructure.Identity.Authentication;
using Template.Infrastructure.Identity.Contexts;
using Template.Infrastructure.Identity.Models;
using Template.Infrastructure.Identity.Seeds;
using Template.Infrastructure.Identity.Services;
using Template.Infrastructure.Identity.Settings;

namespace Template.Infrastructure.Identity;

public static class ServiceRegistration
{
    public static IServiceCollection AddIdentityInfrastructure(this IServiceCollection services, IConfiguration configuration, bool useInMemoryDatabase)
    {
        if (useInMemoryDatabase)
        {
            var databaseName = configuration["TestDatabaseName"] is { Length: > 0 } testDatabaseName
                ? $"{nameof(IdentityContext)}-{testDatabaseName}"
                : nameof(IdentityContext);
            var databaseRoot = new InMemoryDatabaseRoot();
            services.AddDbContext<IdentityContext>(options =>
                options.UseInMemoryDatabase(databaseName, databaseRoot));
        }
        else
        {
            services.AddDbContext<IdentityContext>(options =>
                options.UseNpgsql(configuration.GetConnectionString("IdentityConnection")));
        }

        services.AddTransient<IGetUserServices, GetUserServices>();
        services.AddTransient<IAccountServices, AccountServices>();
        services.AddSingleton<JwtTokenFactory>();

        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
        services.AddOptions<JwtSettings>().Bind(configuration.GetSection(JwtSettings.SectionName)).ValidateOnStart();
        services.AddSingleton<IValidateOptions<IdentitySettings>, IdentitySettingsValidator>();
        services.AddOptions<IdentitySettings>().Bind(configuration.GetSection(IdentitySettings.SectionName)).ValidateOnStart();

        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.SignIn.RequireConfirmedAccount = false;
            options.SignIn.RequireConfirmedEmail = false;
            options.User.RequireUniqueEmail = false;
        }).AddEntityFrameworkStores<IdentityContext>().AddDefaultTokenProviders();

        services.AddOptions<IdentityOptions>()
            .Configure<IOptions<IdentitySettings>>((options, identitySettings) =>
            {
                var settings = identitySettings.Value;
                options.Password.RequireDigit = settings.PasswordRequireDigit;
                options.Password.RequiredLength = settings.PasswordRequiredLength;
                options.Password.RequireNonAlphanumeric = settings.PasswordRequireNonAlphanumeric;
                options.Password.RequireUppercase = settings.PasswordRequireUppercase;
                options.Password.RequireLowercase = settings.PasswordRequireLowercase;
            });

        services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        }).AddJwtBearer();

        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtSettings>>((options, jwtSettings) =>
            {
                // Tokens are validated with a local key, not Authority/MetadataAddress discovery, so this
                // flag has no runtime effect today. It stays at the secure framework default so that adding
                // an authority later cannot silently fetch metadata over HTTP.
                options.RequireHttpsMetadata = true;
                options.SaveToken = false;
                // Keep validation failure reasons out of the WWW-Authenticate header; it stays "Bearer".
                options.IncludeErrorDetails = false;
                options.TokenValidationParameters = JwtTokenFactory.CreateValidationParameters(jwtSettings.Value);
                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = SecurityStampTokenValidator.ValidateAsync
                };
            });

        return services;
    }

    /// <summary>
    /// Applies Identity migrations (relational providers only) and seeds the default role and user.
    /// Keeps <see cref="IdentityContext"/>, <see cref="UserManager{TUser}"/> and <see cref="RoleManager{TRole}"/>
    /// inside this project.
    /// </summary>
    public static async Task InitializeIdentityDatabaseAsync(this IServiceProvider services, bool applyMigrations)
    {
        if (applyMigrations)
            await services.GetRequiredService<IdentityContext>().Database.MigrateAsync();

        await DefaultRoles.SeedAsync(services.GetRequiredService<RoleManager<ApplicationRole>>());
        await DefaultBasicUser.SeedAsync(services.GetRequiredService<UserManager<ApplicationUser>>());
    }

    /// <summary>Readiness probe of the Identity database (EF Core <c>CanConnectAsync</c>), without exposing the context.</summary>
    public static Task<bool> CanConnectToIdentityDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken) =>
        services.GetRequiredService<IdentityContext>().Database.CanConnectAsync(cancellationToken);
}
