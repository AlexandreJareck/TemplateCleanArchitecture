using System.Xml.Linq;
using Shouldly;

namespace Template.Architecture.Tests;

public sealed class ArchitectureRulesTests
{
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Domain_Has_No_Project_Or_Package_Dependencies()
    {
        var project = LoadProject("Src/Core/Template.Domain/Template.Domain.csproj");

        References(project, "ProjectReference").ShouldBeEmpty();
        References(project, "PackageReference").ShouldBeEmpty();
    }

    [Fact]
    public void Domain_Transport_Type_Exception_Is_Explicitly_Phase_4_Bound()
    {
        var actual = FilesContaining("Src/Core/Template.Domain", "DTOs", "*.cs");

        actual.ShouldBeEmpty();
    }

    [Fact]
    public void Application_Has_No_Implementation_Packages()
    {
        var project = LoadProject("Src/Core/Template.Application/Template.Application.csproj");
        var actual = ForbiddenApplicationPackages(References(project, "PackageReference"));

        actual.ShouldBeEmpty(); // Phases 8, 9 and 10 exceptions removed
    }

    [Fact]
    public void Application_Has_No_Implementation_Sources()
    {
        var actual = SourceViolations("Src/Core/Template.Application", ApplicationMarkers());

        actual.ShouldBeEmpty(); // Phases 8, 9 and 10 exceptions removed
    }

    [Fact]
    public void Domain_Has_No_Search_Logging_Or_HealthCheck_References()
    {
        SourceViolations("Src/Core/Template.Domain", SearchVendorMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain", ["Serilog", "HealthCheck"]).ShouldBeEmpty();
    }

    [Fact]
    public void Presentation_Has_No_Persistence_Context_Or_Repository_References()
    {
        var actual = SourceViolations("Src/Presentation", PresentationMarkers());

        actual.ShouldBeEmpty(); // Phase 21: the Phase 7 ApplicationDbContext migration exception was removed
    }

    [Fact]
    public void Core_Has_No_Identity_Jwt_Claims_Or_Http_References()
    {
        SourceViolations("Src/Core", IdentityMarkers()).ShouldBeEmpty(); // Phase 12
        var application = LoadProject("Src/Core/Template.Application/Template.Application.csproj");
        References(application, "PackageReference").Where(IsIdentityPackage).ShouldBeEmpty();
    }

    [Fact]
    public void Identity_Implementation_Lives_In_Infrastructure_Identity()
    {
        var identityTypes = SourceViolations("Src", IdentityImplementationMarkers());
        var jwtTypes = SourceViolations("Src", JwtConstructionMarkers());
        var identityPackageProjects = Directory.GetFiles(Path.Combine(Root, "Src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => References(XDocument.Load(path), "PackageReference").Any(IsIdentityPackage))
            .Select(Relative)
            .ToArray();

        identityTypes.ShouldAllBe(path => path.StartsWith(IdentityProject, StringComparison.Ordinal));
        identityTypes.ShouldNotBeEmpty();
        jwtTypes.ShouldAllBe(path => path.StartsWith(IdentityProject, StringComparison.Ordinal));
        jwtTypes.ShouldNotBeEmpty();
        identityPackageProjects.ShouldBe([$"{IdentityProject}Template.Infrastructure.Identity.csproj"]);
        References(LoadProject($"{IdentityProject}Template.Infrastructure.Identity.csproj"), "PackageReference")
            .ShouldNotContain("Microsoft.AspNetCore.Identity"); // legacy 2.3.x metapackage stays removed
    }

    [Fact]
    public void Composition_Roots_Own_Their_Current_User_Adapters()
    {
        SourceViolations("Src", [": IAuthenticatedUserService"])
            .ShouldBe(["Src/Presentation/Template.WebApi/Infrastructure/Services/AuthenticatedUserService.cs",
                "Src/Infrastructure/Template.Infrastructure.Persistence/Contexts/ApplicationDbContextFactory.cs",
                "Src/Workers/Template.Worker/WorkerRequestContext.cs"], ignoreOrder: true);
        SourceViolations("Src/Infrastructure", ["IHttpContextAccessor"]).ShouldBeEmpty();
    }

    [Fact]
    public void Only_Approved_Composition_Roots_And_Tests_Compose_Infrastructure()
    {
        var projects = Directory.GetFiles(Root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}ExtensionVsix{Path.DirectorySeparatorChar}"))
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}.claude{Path.DirectorySeparatorChar}")); // local agent worktrees are not part of this checkout

        var composers = projects
            .Where(path => References(XDocument.Load(path), "ProjectReference")
                .Any(reference => reference.Contains("Infrastructure", StringComparison.Ordinal)))
            .Select(Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

        composers.ShouldBe([
            "Src/Presentation/Template.WebApi/Template.WebApi.csproj",
            "Src/Workers/Template.Worker/Template.Worker.csproj",
            "Tests/Template.Elasticsearch.Integration.Tests/Template.Elasticsearch.Integration.Tests.csproj",
            "Tests/Template.Integration.Tests/Template.Integration.Tests.csproj",
            "Tests/Template.PostgreSql.Integration.Tests/Template.PostgreSql.Integration.Tests.csproj",
            "Tests/Template.RabbitMq.Integration.Tests/Template.RabbitMq.Integration.Tests.csproj",
            "Tests/Template.Redis.Integration.Tests/Template.Redis.Integration.Tests.csproj"
        ]);
    }

    [Fact]
    public void Core_Has_No_Messaging_Plumbing_Or_Hosted_Services()
    {
        SourceViolations("Src/Core", MessagingPlumbingMarkers()).ShouldBeEmpty(); // Phase 9
        SourceViolations("Src/Core/Template.Domain", ["RabbitMQ", "Microsoft.Extensions.Hosting", "Messaging"]).ShouldBeEmpty();
    }

    [Fact]
    public void Messaging_Vendor_Code_And_Hosted_Services_Live_In_Infrastructure_Messaging()
    {
        var rabbitMqProjects = Directory.GetFiles(Path.Combine(Root, "Src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => References(XDocument.Load(path), "PackageReference").Contains("RabbitMQ.Client"))
            .Select(Relative)
            .ToArray();
        var hostedServiceSources = SourceViolations("Src", [": BackgroundService", "AddHostedService"]);
        var rabbitMqSources = SourceViolations("Src", ["using RabbitMQ"]);

        rabbitMqProjects.ShouldBe(["Src/Infrastructure/Template.Infrastructure.Messaging/Template.Infrastructure.Messaging.csproj"]);
        hostedServiceSources.ShouldAllBe(path => path.StartsWith(MessagingProject, StringComparison.Ordinal)
            || path.StartsWith(WorkerProject, StringComparison.Ordinal));
        hostedServiceSources.ShouldNotBeEmpty();
        rabbitMqSources.ShouldAllBe(path => path.StartsWith(MessagingProject, StringComparison.Ordinal));
        SourceViolations("Src", ["class RabbitMqSettings"]).ShouldBe([$"{MessagingProject}RabbitMqSettings.cs"]);
    }

    [Fact]
    public void Infrastructure_Messaging_Implements_Application_Owned_Ports()
    {
        var project = LoadProject($"{MessagingProject}Template.Infrastructure.Messaging.csproj");

        References(project, "ProjectReference").ShouldBe([@"..\..\Core\Template.Application\Template.Application.csproj"]);
        ReadSource($"{MessagingProject}Publishing/RabbitMqOrderEventPublisher.cs").ShouldContain(": IOrderEventPublisher");
        ReadSource($"{MessagingProject}Publishing/DisabledOrderEventPublisher.cs").ShouldContain(": IOrderEventPublisher");
        ReadSource($"{MessagingProject}Deduplication/InMemoryProcessedMessageStore.cs").ShouldContain(": IProcessedMessageStore");
        ReadSource("Src/Core/Template.Application/Interfaces/Messaging/IOrderEventPublisher.cs").ShouldContain("public interface IOrderEventPublisher");
        SourceViolations("Src/Core/Template.Application/Interfaces/Messaging", ["RabbitMQ", "ReadOnlyMemory<byte>", "IChannel", "IConnection", "BasicDeliverEventArgs", "Json"]).ShouldBeEmpty();
    }

    [Fact]
    public void WebApi_Only_Composes_Messaging()
    {
        var webApi = LoadProject("Src/Presentation/Template.WebApi/Template.WebApi.csproj");

        References(webApi, "PackageReference").ShouldNotContain(package => package.Contains("RabbitMQ", StringComparison.Ordinal));
        SourceViolations("Src/Presentation", MessagingPlumbingMarkers()).ShouldBeEmpty();
        ReadSource("Src/Presentation/Template.WebApi/Program.cs").ShouldContain("AddMessagingInfrastructure(");
        ReadSource("Src/Presentation/Template.WebApi/Program.cs").ShouldNotContain("AddProcurementMessagingInfrastructure(");
    }

    [Fact]
    public void Worker_Is_A_Thin_Composition_Root_And_Owns_No_RabbitMq_Vendor_Code()
    {
        var worker = LoadProject($"{WorkerProject}Template.Worker.csproj");
        References(worker, "ProjectReference").ShouldBe([
            @"..\..\Core\Template.Application\Template.Application.csproj",
            @"..\..\Infrastructure\Template.Infrastructure.Messaging\Template.Infrastructure.Messaging.csproj",
            @"..\..\Infrastructure\Template.Infrastructure.Persistence\Template.Infrastructure.Persistence.csproj"
        ]);
        References(worker, "PackageReference").ShouldNotContain(package => package.Contains("RabbitMQ", StringComparison.Ordinal));
        SourceViolations(WorkerProject.TrimEnd('/'), ["using RabbitMQ", "IChannel", "BasicPublish", "BasicConsume"]).ShouldBeEmpty();
        ReadSource($"{WorkerProject}Program.cs").ShouldContain("AddProcurementMessagingInfrastructure(");
    }

    [Fact]
    public void Search_Vendor_Code_Lives_In_Infrastructure_Search()
    {
        var elasticClientProjects = Directory.GetFiles(Path.Combine(Root, "Src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => References(XDocument.Load(path), "PackageReference").Contains("Elastic.Clients.Elasticsearch"))
            .Select(Relative)
            .ToArray();
        var clientSources = SourceViolations("Src", ["using Elastic.Clients", "ElasticsearchClient"]);

        elasticClientProjects.ShouldBe([$"{SearchProject}Template.Infrastructure.Search.csproj"]);
        clientSources.ShouldAllBe(path => path.StartsWith(SearchProject, StringComparison.Ordinal));
        clientSources.ShouldNotBeEmpty();
        SourceViolations("Src", ["class PersonSearchSettings"]).ShouldBe([$"{SearchProject}PersonSearchSettings.cs"]);
        SourceViolations("Src", ["class ElasticSearchSettings"]).ShouldBeEmpty(); // the shared legacy options type is gone; only its keys remain
    }

    [Fact]
    public void Infrastructure_Search_Implements_Application_Owned_Ports()
    {
        var project = LoadProject($"{SearchProject}Template.Infrastructure.Search.csproj");

        References(project, "ProjectReference").ShouldBe([@"..\..\Core\Template.Application\Template.Application.csproj"]);
        ReadSource($"{SearchProject}Persons/ElasticsearchPersonSearchIndex.cs").ShouldContain(": IPersonSearchIndex");
        ReadSource($"{SearchProject}Persons/DisabledPersonSearchIndex.cs").ShouldContain(": IPersonSearchIndex");
        ReadSource("Src/Core/Template.Application/Interfaces/Search/IPersonSearchIndex.cs").ShouldContain("public interface IPersonSearchIndex");
        SourceViolations("Src/Core/Template.Application/Interfaces/Search", PortLeakMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Application/Search", PortLeakMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void Logging_And_HealthChecks_Do_Not_Depend_On_The_Search_Port()
    {
        SourceViolations("Src/Presentation/Template.WebApi/Infrastructure/Logging", SearchPortMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Presentation/Template.WebApi/Infrastructure/HealthChecks", SearchPortMarkers()).ShouldBeEmpty();
        ReadSource("Src/Presentation/Template.WebApi/Infrastructure/Logging/LoggingExtensions.cs").ShouldContain("WriteTo.Elasticsearch(");
        ReadSource("Src/Presentation/Template.WebApi/Infrastructure/HealthChecks/HealthCheckExtensions.cs").ShouldContain("new ElasticsearchHealthCheck(");
    }

    [Fact]
    public void WebApi_Only_Composes_Search()
    {
        var webApi = LoadProject("Src/Presentation/Template.WebApi/Template.WebApi.csproj");

        References(webApi, "PackageReference").ShouldNotContain("Elastic.Clients.Elasticsearch");
        References(webApi, "ProjectReference").ShouldContain(@"..\..\Infrastructure\Template.Infrastructure.Search\Template.Infrastructure.Search.csproj");
        ReadSource("Src/Presentation/Template.WebApi/Program.cs").ShouldContain("AddSearchInfrastructure(");
        SourceViolations("Src/Presentation/Template.WebApi/Controllers", [.. SearchVendorMarkers(), "IPersonSearchIndex"]).ShouldBeEmpty();
    }

    [Fact]
    public void Registration_Does_Not_Build_Intermediate_Service_Providers()
    {
        var actual = SourceViolations("Src", ["BuildServiceProvider("]);

        actual.ShouldBeEmpty(); // Phase 13: the LocalizationExtensions exception was removed; no replacement exception
    }

    [Fact]
    public void Domain_Has_No_Logging_Health_Http_Or_Localization_Framework()
    {
        SourceViolations("Src/Core/Template.Domain", ObservabilityFrameworkMarkers()).ShouldBeEmpty(); // Phase 13
    }

    [Fact]
    public void Application_Depends_Only_On_The_Localization_Port()
    {
        SourceViolations("Src/Core", LocalizationImplementationMarkers()).ShouldBeEmpty(); // Phase 13
        SourceViolations("Src/Core/Template.Application", ["Serilog", "HealthCheck", "Microsoft.AspNetCore.Localization"]).ShouldBeEmpty();
        ReadSource("Src/Core/Template.Application/Interfaces/ITranslator.cs").ShouldContain("public interface ITranslator");
    }

    [Fact]
    public void Concrete_Localization_Lives_In_Infrastructure_Resources()
    {
        SourceViolations("Src", ["ResourceManager", ": ITranslator"])
            .ShouldAllBe(path => path.StartsWith("Src/Infrastructure/Template.Infrastructure.Resources/", StringComparison.Ordinal));
        SourceViolations("Src", [": ITranslator"]).ShouldBe(["Src/Infrastructure/Template.Infrastructure.Resources/Services/Translator.cs"]);
    }

    [Fact]
    public void Logging_And_Health_Do_Not_Depend_On_Application_Ports()
    {
        SourceViolations("Src/Presentation/Template.WebApi/Infrastructure/Logging", ApplicationPortMarkers()).ShouldBeEmpty(); // Phase 13
        SourceViolations("Src/Presentation/Template.WebApi/Infrastructure/HealthChecks", ApplicationPortMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Presentation/Template.WebApi/Infrastructure/HealthChecks", PresentationMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void Redundant_Logging_Packages_Stay_Removed()
    {
        var webApi = References(LoadProject("Src/Presentation/Template.WebApi/Template.WebApi.csproj"), "PackageReference");
        var central = ReadSource("Directory.Packages.props");

        foreach (var package in RemovedLoggingPackages)
        {
            webApi.ShouldNotContain(package); // Phase 13
            central.ShouldNotContain($"\"{package}\"");
        }

        webApi.ShouldContain("Serilog.AspNetCore");
    }

    [Fact]
    public void Application_Features_Do_Not_Read_The_System_Clock()
    {
        var actual = SourceViolations("Src/Core/Template.Application/Features", SystemClockMarkers());

        actual.ShouldBeEmpty(); // Phase 5: use IClock
    }

    [Fact]
    public void Application_Features_Do_Not_Reference_AspNetCore()
    {
        var actual = SourceViolations("Src/Core/Template.Application/Features", ["Microsoft.AspNetCore", "HttpContext", "IActionResult"]);

        actual.ShouldBeEmpty();
    }

    [Fact]
    public void Controllers_Do_Not_Use_Service_Location()
    {
        var actual = SourceViolations(
            "Src/Presentation/Template.WebApi/Controllers",
            ServiceLocatorMarkers());

        actual.ShouldBeEmpty();
    }

    [Fact]
    public void Application_Ports_Do_Not_Expose_IQueryable()
    {
        var actual = SourceViolations(
            "Src/Core/Template.Application/Interfaces",
            ["IQueryable<", "System.Linq.IQueryable"]);

        actual.ShouldBeEmpty();
    }

    [Fact]
    public void Persistence_UsesExplicitRepositoryRegistration_AndNoSqlServer()
    {
        var registration = File.ReadAllText(Path.Combine(
            Root,
            "Src/Infrastructure/Template.Infrastructure.Persistence/ServiceRegistration.cs"));
        var forbiddenSqlServer = SourceViolations(
            "Src",
            ["UseSqlServer", "Microsoft.EntityFrameworkCore.SqlServer"]);

        registration.ShouldContain("AddScoped(typeof(IGenericRepository<>), typeof(GenericRepository<>))");
        registration.ShouldContain("AddScoped<IProductRepository, ProductRepository>()");
        registration.ShouldNotContain("System.Reflection");
        registration.ShouldNotContain("Assembly.GetAssembly");
        forbiddenSqlServer.ShouldBeEmpty();
    }

    [Fact]
    public void Controlled_Mutations_Are_Detected_By_Each_Rule()
    {
        ContainsAny("CreatedDateTime = DateTime.UtcNow,", SystemClockMarkers()).ShouldBeTrue();
        ContainsAny("CreatedDateTime = clock.UtcNow,", SystemClockMarkers()).ShouldBeFalse();
        ContainsAny("public IActionResult Handle()", ["Microsoft.AspNetCore", "HttpContext", "IActionResult"]).ShouldBeTrue();
        ForbiddenApplicationPackages(["MediatR", "RabbitMQ.Client"]).ShouldBe(["RabbitMQ.Client"]);
        ContainsAny("using Microsoft.EntityFrameworkCore;", ApplicationMarkers()).ShouldBeTrue();
        ContainsAny("private readonly ApplicationDbContext db;", PresentationMarkers()).ShouldBeTrue();
        ContainsAny("HttpContext.RequestServices.GetRequiredService<IMediator>()", ServiceLocatorMarkers()).ShouldBeTrue();
        ContainsAny("Task<IQueryable<Product>> Query()", ["IQueryable<", "System.Linq.IQueryable"]).ShouldBeTrue();
        ContainsAny("options.UseSqlServer(connection)", ["UseSqlServer", "Microsoft.EntityFrameworkCore.SqlServer"]).ShouldBeTrue();
        "Assembly.GetAssembly(typeof(IRepository))".ShouldContain("Assembly.GetAssembly");
        IsInfrastructureReference("..\\Infrastructure\\Example.csproj").ShouldBeTrue();
        ForbiddenApplicationPackages(["Microsoft.Extensions.Hosting.Abstractions"]).ShouldBe(["Microsoft.Extensions.Hosting.Abstractions"]);
        ContainsAny("public sealed class OrderConsumer : BackgroundService", MessagingPlumbingMarkers()).ShouldBeTrue();
        ContainsAny("await connection.CreateChannelAsync(ct);", MessagingPlumbingMarkers()).ShouldBeTrue();
        ContainsAny("await channel.QueueDeclareAsync(\"orders\");", MessagingPlumbingMarkers()).ShouldBeTrue();
        ContainsAny("public sealed class RabbitMqSettings", MessagingPlumbingMarkers()).ShouldBeTrue();
        ContainsAny("Task PublishBroadcastAsync(OrderPlacedEvent e, CancellationToken ct);", MessagingPlumbingMarkers()).ShouldBeFalse();
        ForbiddenApplicationPackages(["Elastic.Clients.Elasticsearch", "Elastic.Transport", "Elastic.Serilog.Sinks", "AspNetCore.HealthChecks.Elasticsearch", "MediatR"])
            .ShouldBe(["AspNetCore.HealthChecks.Elasticsearch", "Elastic.Clients.Elasticsearch", "Elastic.Serilog.Sinks", "Elastic.Transport"]);
        ContainsAny("using Elastic.Clients.Elasticsearch;", ApplicationMarkers()).ShouldBeTrue();
        ContainsAny("using Elastic.Transport;", ApplicationMarkers()).ShouldBeTrue();
        ContainsAny("using Elastic.Serilog.Sinks;", ApplicationMarkers()).ShouldBeTrue();
        ContainsAny("var client = new ElasticsearchClient(settings);", ApplicationMarkers()).ShouldBeTrue();
        ContainsAny("public class ElasticSearchSettings { public string Url { get; init; } }", ApplicationMarkers()).ShouldBeTrue();
        ContainsAny("services.AddHealthChecks().AddElasticsearch(url);", ApplicationMarkers()).ShouldBeTrue();
        ContainsAny("Task<PersonIndexResult> UpsertAsync(IndexedPerson person, CancellationToken cancellationToken);", ApplicationMarkers()).ShouldBeFalse();
        ContainsAny("Task<SearchResponse<JsonElement>> SearchAsync();", PortLeakMarkers()).ShouldBeTrue();
        ContainsAny("Task<PersonIndexResult<IndexedPerson>> GetAsync(string key, CancellationToken cancellationToken);", PortLeakMarkers()).ShouldBeFalse();
        ContainsAny("public LoggingExtensions(IPersonSearchIndex index)", SearchPortMarkers()).ShouldBeTrue();
        ContainsAny("var url = settings.Get<PersonSearchSettings>().Url;", SearchPortMarkers()).ShouldBeTrue();
        ContainsAny("using var provider = services.BuildServiceProvider();", ["BuildServiceProvider("]).ShouldBeTrue();
        ContainsAny("using Microsoft.Extensions.Logging;", ObservabilityFrameworkMarkers()).ShouldBeTrue();
        ContainsAny("public sealed class Product { public Guid Id { get; } }", ObservabilityFrameworkMarkers()).ShouldBeFalse();
        ContainsAny("public Handler(IStringLocalizer<Messages> localizer)", LocalizationImplementationMarkers()).ShouldBeTrue();
        ContainsAny("new ResourceManager(typeof(Messages))", LocalizationImplementationMarkers()).ShouldBeTrue();
        ContainsAny("public Handler(ITranslator translator)", LocalizationImplementationMarkers()).ShouldBeFalse();
        ContainsAny("public sealed class CacheHealthCheck(IProductCache cache) : IHealthCheck", ApplicationPortMarkers()).ShouldBeTrue();
        ContainsAny("public sealed class DistributedCacheHealthCheck(IDistributedCache cache)", ApplicationPortMarkers()).ShouldBeFalse();
        ContainsAny("public interface ICurrentUser { ClaimsPrincipal Principal { get; } }", IdentityMarkers()).ShouldBeTrue();
        ContainsAny("Task<string> CreateAsync(UserManager<User> users);", IdentityMarkers()).ShouldBeTrue();
        ContainsAny("using System.IdentityModel.Tokens.Jwt;", IdentityMarkers()).ShouldBeTrue();
        ContainsAny("using Microsoft.IdentityModel.Tokens;", IdentityMarkers()).ShouldBeTrue();
        ContainsAny("string? UserId { get; }", IdentityMarkers()).ShouldBeFalse();
        ContainsAny("var key = new SymmetricSecurityKey(bytes);", JwtConstructionMarkers()).ShouldBeTrue();
        ContainsAny("public Handler(SignInManager<ApplicationUser> signInManager)", IdentityImplementationMarkers()).ShouldBeTrue();
        ContainsAny("await services.GetRequiredService<IdentityContext>().Database.MigrateAsync();", PresentationMarkers()).ShouldBeTrue();
        ContainsAny("await services.GetRequiredService<ApplicationDbContext>().Database.MigrateAsync();", PresentationMarkers()).ShouldBeTrue();
        ContainsAny("await services.InitializeApplicationDatabaseAsync(applyMigrations: true);", PresentationMarkers()).ShouldBeFalse();
        IsIdentityPackage("Microsoft.AspNetCore.Identity").ShouldBeTrue();
        IsIdentityPackage("System.IdentityModel.Tokens.Jwt").ShouldBeTrue();
        IsIdentityPackage("MediatR").ShouldBeFalse();
    }

    [Fact]
    public void Controllers_Authorize_By_Permission_Never_By_Role()
    {
        // SEC-008-a: roles only group permissions; no controller may name a role.
        SourceViolations("Src/Presentation/Template.WebApi/Controllers", RoleAuthorizationMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core", RoleAuthorizationMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void Phase24_Controllers_Declare_Authorization_On_Every_Action()
    {
        // NFR-SEC-3: every action of a convention-following controller states its permission policy, [Authorize] or [AllowAnonymous].
        var controllers = Directory.GetFiles(Path.Combine(Root, "Src/Presentation/Template.WebApi/Controllers"), "*.cs", SearchOption.AllDirectories)
            .Where(path => File.ReadAllText(path).Contains("[StableProblemCodes]", StringComparison.Ordinal))
            .ToArray();

        controllers.Length.ShouldBe(16); // Phase 24: 3; Phase 25: +9 master-data, enrichment and user-lookup; Phase 27: +3 budget and requisition controllers; Phase 29: +1 requests for quotation
        foreach (var path in controllers)
            ActionsWithoutExplicitAuthorization(File.ReadAllText(path)).ShouldBe(0, Relative(path));
    }

    [Fact]
    public void Audit_Port_Is_Append_Only()
    {
        // AUD-002-a: no update or delete path for audit records.
        ReadSource("Src/Core/Template.Application/Interfaces/Auditing/IAuditTrail.cs").ShouldContain("void Append(AuditRecord record);");
        ContainsAny(ReadSource("Src/Core/Template.Application/Interfaces/Auditing/IAuditTrail.cs"), AuditMutationMarkers()).ShouldBeFalse();
        ContainsAny(ReadSource("Src/Infrastructure/Template.Infrastructure.Persistence/Repositories/AuditRecordRepository.cs"), AuditMutationMarkers()).ShouldBeFalse();
    }

    [Fact]
    public void Session_Cryptography_And_Token_Issuance_Live_In_Infrastructure_Identity()
    {
        // D23-11 / SEC-002: key material, random tokens, HMAC and JWT stay out of Core and Persistence.
        SourceViolations("Src/Core", SessionCryptoMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Infrastructure/Template.Infrastructure.Persistence", SessionCryptoMarkers()).ShouldBeEmpty();
        SourceViolations("Src", [": ISessionTokenService", ": IAccessTokenIssuer", ": IAccountDirectory"])
            .ShouldAllBe(path => path.StartsWith(IdentityProject, StringComparison.Ordinal));
        SourceViolations("Src", [": ISessionFamilyRepository", "IAuditTrail, IAuditRecordReader", ": IIdempotencyStore"])
            .ShouldAllBe(path => path.StartsWith("Src/Infrastructure/Template.Infrastructure.Persistence/", StringComparison.Ordinal));
    }

    [Fact]
    public void Cors_Never_Allows_Any_Origin()
    {
        SourceViolations("Src/Presentation", ["AllowAnyOrigin", "SetIsOriginAllowed(_ => true)", "AddAnyCors"]).ShouldBeEmpty();
    }

    [Fact]
    public void Phase24_Controlled_Mutations_Are_Detected()
    {
        ContainsAny("[Authorize(Roles = \"Admin\")]", RoleAuthorizationMarkers()).ShouldBeTrue();
        ContainsAny("policy.RequireRole(\"Buyer\")", RoleAuthorizationMarkers()).ShouldBeTrue();
        ContainsAny("[HasPermission(Permissions.AuditRead)]", RoleAuthorizationMarkers()).ShouldBeFalse();
        ActionsWithoutExplicitAuthorization("""
            [HttpGet]
            [HasPermission(Permissions.AuditRead)]
            public Task<IActionResult> List() => null!;
            [HttpPost]
            public Task<IActionResult> Create() => null!;
            """).ShouldBe(1);
        ActionsWithoutExplicitAuthorization("""
            [HttpGet]
            [Authorize]
            public Task<IActionResult> Get() => null!;
            [HttpDelete("current")]
            [AllowAnonymous]
            public Task<IActionResult> Delete() => null!;
            """).ShouldBe(0);
        ContainsAny("void Delete(AuditRecord record);", AuditMutationMarkers()).ShouldBeTrue();
        ContainsAny("context.Remove(record);", AuditMutationMarkers()).ShouldBeTrue();
        ContainsAny("await query.ExecuteDeleteAsync();", AuditMutationMarkers()).ShouldBeTrue();
        ContainsAny("var bytes = RandomNumberGenerator.GetBytes(32);", SessionCryptoMarkers()).ShouldBeTrue();
        ContainsAny("HMACSHA256.HashData(key, data)", SessionCryptoMarkers()).ShouldBeTrue();
        ContainsAny("string? HashRefreshToken(string? token);", SessionCryptoMarkers()).ShouldBeFalse();
    }

    private static readonly string[] MasterDataModules = ["Organization", "Suppliers", "Catalog", "Budgeting", "Requisitions", "Sourcing"];

    [Fact]
    public void Domain_Modules_Reference_Each_Other_Only_Through_The_Shared_Kernel()
    {
        // ADR-0001 / domain-model §1: a module references another module's aggregates by ID only.
        foreach (var module in MasterDataModules)
            SourceViolations($"Src/Core/Template.Domain/{module}", OtherDomainModuleMarkers(module)).ShouldBeEmpty(module);
        SourceViolations("Src/Core/Template.Domain/Common", MasterDataModules.Select(module => $"Template.Domain.{module}").ToArray()).ShouldBeEmpty();
    }

    [Fact]
    public void External_Provider_Code_Lives_In_Infrastructure_Integrations()
    {
        // ADR-0007 / INT-001: only the Integrations adapter talks HTTP to BrasilAPI or BCB; Core owns ports only.
        SourceViolations("Src", ProviderMarkers()).ShouldAllBe(path => path.StartsWith(IntegrationsProject, StringComparison.Ordinal));
        SourceViolations("Src", ProviderMarkers()).ShouldNotBeEmpty();
        SourceViolations("Src/Core", HttpClientMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Infrastructure/Template.Infrastructure.Persistence", HttpClientMarkers()).ShouldBeEmpty();
        Directory.GetFiles(Path.Combine(Root, "Src"), "*.csproj", SearchOption.AllDirectories)
            .Where(path => References(XDocument.Load(path), "PackageReference").Contains("Microsoft.Extensions.Http"))
            .Select(Relative)
            .ShouldBe([$"{IntegrationsProject}Template.Infrastructure.Integrations.csproj"]);
    }

    [Fact]
    public void Infrastructure_Integrations_Implements_Application_Owned_Ports_Without_Leaking_Transport()
    {
        var project = LoadProject($"{IntegrationsProject}Template.Infrastructure.Integrations.csproj");

        References(project, "ProjectReference").ShouldBe([@"..\..\Core\Template.Application\Template.Application.csproj"]);
        ReadSource($"{IntegrationsProject}BrasilApi/BrasilApiClient.cs").ShouldContain(": ISupplierRegistryLookup, IPostalCodeLookup");
        ReadSource($"{IntegrationsProject}Bcb/BcbPtaxClient.cs").ShouldContain(": IExchangeRateProvider");
        ReadSource($"{IntegrationsProject}DisabledEnrichmentProviders.cs").ShouldContain(": ISupplierRegistryLookup, IPostalCodeLookup, IExchangeRateProvider");
        SourceViolations("Src/Core/Template.Application/Interfaces/Integrations", EnrichmentPortLeakMarkers()).ShouldBeEmpty();
        ReadSource("Src/Presentation/Template.WebApi/Program.cs").ShouldContain("AddIntegrationsInfrastructure(");
        SourceViolations("Src/Presentation/Template.WebApi/Controllers", ProviderMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void Provider_Clients_Never_Log_Request_Urls()
    {
        // NFR §9: request URLs carry the CNPJ or CEP, so the default HttpClient request logging is removed and the
        // adapters' own log templates name provider, operation and classification only.
        var registration = ReadSource($"{IntegrationsProject}ServiceRegistration.cs");
        registration.Split(".RemoveAllLoggers()").Length.ShouldBe(3);
        registration.Split("AddHttpClient<").Length.ShouldBe(3);
        SourceViolations(IntegrationsProject, UrlLoggingMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void MasterData_Controllers_Expose_No_Hard_Delete()
    {
        // ORG-005: referenced master data is deactivated, never deleted.
        foreach (var controller in MasterDataControllers)
            ReadSource($"Src/Presentation/Template.WebApi/Controllers/v2/{controller}.cs").ShouldNotContain("[HttpDelete", Case.Sensitive, controller);
    }

    [Fact]
    public void Phase25_Controlled_Mutations_Are_Detected()
    {
        ContainsAny("using Template.Domain.Catalog;", OtherDomainModuleMarkers("Suppliers")).ShouldBeTrue();
        ContainsAny("using Template.Domain.Common;", OtherDomainModuleMarkers("Suppliers")).ShouldBeFalse();
        ContainsAny("private readonly HttpClient client;", HttpClientMarkers()).ShouldBeTrue();
        ContainsAny("var url = \"https://brasilapi.com.br/api/cnpj/v1/\";", ProviderMarkers()).ShouldBeTrue();
        ContainsAny("Task<HttpResponseMessage> LookupAsync(string cnpj);", EnrichmentPortLeakMarkers()).ShouldBeTrue();
        ContainsAny("Task<EnrichmentResult<PostalAddressSuggestion>> LookupAsync(string postalCode, CancellationToken cancellationToken);", EnrichmentPortLeakMarkers()).ShouldBeFalse();
        ContainsAny("logger.LogWarning(\"Call to {Url} failed\", request.RequestUri);", UrlLoggingMarkers()).ShouldBeTrue();
        ContainsAny("logger.LogWarning(\"Provider {Provider} failed\", Provider);", UrlLoggingMarkers()).ShouldBeFalse();
        "[HttpDelete(\"{id:guid}\")]".Contains("[HttpDelete", StringComparison.Ordinal).ShouldBeTrue();
    }

    [Fact]
    public void Phase27_Raw_Sql_And_Row_Locks_Live_In_Persistence_Only()
    {
        // BUD-006 / BUD-011: row locks and the counter upsert are Persistence details behind Application ports.
        SourceViolations("Src", RawSqlMarkers())
            .ShouldAllBe(path => path.StartsWith("Src/Infrastructure/Template.Infrastructure.Persistence/", StringComparison.Ordinal));
        SourceViolations("Src/Core", RawSqlMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Presentation", RawSqlMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void Phase27_Budget_Reservations_Change_Only_Through_Requisition_Transitions()
    {
        // BUD-004..008 / REQ-005: reserve and release run inside the requisition transition use cases (one transaction);
        // no controller or other use case reserves or releases funds directly.
        SourceViolations("Src/Core/Template.Application", ReservationMutationMarkers())
            .ShouldBe(["Src/Core/Template.Application/Features/Requisitions/RequisitionTransitionUseCases.cs"]);
        SourceViolations("Src/Presentation/Template.WebApi/Controllers", ReservationMutationMarkers()).ShouldBeEmpty();
        var budgetController = ReadSource("Src/Presentation/Template.WebApi/Controllers/v2/BudgetAllocationsController.cs");
        budgetController.ShouldNotContain("/reserve\"", Case.Insensitive);
        budgetController.ShouldNotContain("/release\"", Case.Insensitive);
    }

    [Fact]
    public void Phase27_Procurement_Aggregates_Expose_No_Public_Setters()
    {
        // Core instructions: transitions are behavior methods; public setters would bypass REQ-006 and BUD-003..008.
        SourceViolations("Src/Core/Template.Domain/Budgeting", PublicSetterMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain/Requisitions", PublicSetterMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void Phase27_Controlled_Mutations_Are_Detected()
    {
        ContainsAny("using Template.Domain.Organization;", OtherDomainModuleMarkers("Requisitions")).ShouldBeTrue();
        ContainsAny("using Template.Domain.Budgeting;", OtherDomainModuleMarkers("Requisitions")).ShouldBeTrue();
        ContainsAny("using Template.Domain.Common;", OtherDomainModuleMarkers("Budgeting")).ShouldBeFalse();
        ContainsAny("await context.Database.ExecuteSqlRawAsync(sql);", RawSqlMarkers()).ShouldBeTrue();
        ContainsAny("query.FromSqlInterpolated($\"SELECT 1\")", RawSqlMarkers()).ShouldBeTrue();
        ContainsAny("allocation.Reserve(id, requisitionId, 1, amount, period, date, actor, now, correlation);", ReservationMutationMarkers()).ShouldBeTrue();
        ContainsAny("allocation.Release(requisitionId, BudgetReleaseReason.Cancelled, actor, now, correlation);", ReservationMutationMarkers()).ShouldBeTrue();
        ContainsAny("var available = allocation.AvailableAmount;", ReservationMutationMarkers()).ShouldBeFalse();
        ContainsAny("public RequisitionStatus Status { get; set; }", PublicSetterMarkers()).ShouldBeTrue();
        ContainsAny("public RequisitionStatus Status { get; private set; }", PublicSetterMarkers()).ShouldBeFalse();
        ContainsAny("public decimal Amount { get; init; }", PublicSetterMarkers()).ShouldBeTrue();
    }

    [Fact]
    public void Phase29_Sourcing_Aggregate_Exposes_No_Public_Setters_And_Stays_Framework_Free()
    {
        // SRC-002/009: lines, revisions, comparisons and waivers change only through RequestForQuotation behavior methods.
        SourceViolations("Src/Core/Template.Domain/Sourcing", PublicSetterMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain/Sourcing", OtherDomainModuleMarkers("Sourcing")).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain/Sourcing", ProviderMarkers()).ShouldBeEmpty();
    }

    [Fact]
    public void Phase29_Award_Reservation_Adjustment_Lives_Only_In_The_Award_Use_Case()
    {
        // BUD-009 / SRC-019: the reservation follows the award in one transaction; no controller or other use case adjusts it.
        SourceViolations("Src/Core/Template.Application", AwardAdjustmentMarkers())
            .ShouldBe(["Src/Core/Template.Application/Features/Sourcing/RfqUseCases.cs"]);
        SourceViolations("Src/Presentation/Template.WebApi/Controllers", AwardAdjustmentMarkers()).ShouldBeEmpty();
        ReadSource("Src/Presentation/Template.WebApi/Controllers/v2/RequestsForQuotationController.cs").ShouldNotContain("[HttpDelete(\"{id:guid}\")]");
    }

    [Fact]
    public void Phase29_Browser_Never_Calculates_Authoritative_Ranking_Or_Conversion()
    {
        // Handoff: the browser may format and preview values but never computes rankings or exchange conversions.
        var client = Path.Combine(Root, "Client/Template.WebApp/src/app");
        if (!Directory.Exists(client))
            return;
        Directory.GetFiles(client, "*.ts", SearchOption.AllDirectories)
            .Where(path => !path.EndsWith(".spec.ts", StringComparison.Ordinal) && !path.Contains("generated", StringComparison.Ordinal))
            .Where(path => ContainsAny(File.ReadAllText(path), BrowserRankingMarkers()))
            .Select(Relative)
            .ShouldBeEmpty();
    }

    [Fact]
    public void Phase29_Controlled_Mutations_Are_Detected()
    {
        ContainsAny("using Template.Domain.Suppliers;", OtherDomainModuleMarkers("Sourcing")).ShouldBeTrue();
        ContainsAny("using Template.Domain.Requisitions;", OtherDomainModuleMarkers("Sourcing")).ShouldBeTrue();
        ContainsAny("using Template.Domain.Common;", OtherDomainModuleMarkers("Sourcing")).ShouldBeFalse();
        ContainsAny("public decimal Total { get; set; }", PublicSetterMarkers()).ShouldBeTrue();
        ContainsAny("allocation.AdjustForAward(requisitionId, amount, awardId, actor, now, correlation);", AwardAdjustmentMarkers()).ShouldBeTrue();
        ContainsAny("var amount = award.RequisitionCurrencyTotal;", AwardAdjustmentMarkers()).ShouldBeFalse();
        ContainsAny("const normalized = total * rate;", BrowserRankingMarkers()).ShouldBeTrue();
        ContainsAny("entries.sort((a, b) => a.normalizedTotal - b.normalizedTotal);", BrowserRankingMarkers()).ShouldBeTrue();
        ContainsAny("formatMoney(entry.normalizedTotal)", BrowserRankingMarkers()).ShouldBeFalse();
    }

    private static string[] AwardAdjustmentMarkers() => [".AdjustForAward("];

    private static string[] BrowserRankingMarkers() => ["* rate", "*rate", "normalizedTotal -", "rank =", "rank++"];

    private static string[] RawSqlMarkers() => ["ExecuteSqlRaw", "ExecuteSqlInterpolated", "FromSqlRaw", "FromSqlInterpolated", "SqlQuery<", "SqlQueryRaw<"];

    private static string[] ReservationMutationMarkers() => [".Reserve(", ".Release(", ".Commit("];

    private static string[] PublicSetterMarkers() => ["{ get; set; }", "{ get; init; }", "{ set; }", " public set "];

    private const string IntegrationsProject = "Src/Infrastructure/Template.Infrastructure.Integrations/";

    private static readonly string[] MasterDataControllers =
    [
        "DepartmentsController", "CostCentersController", "OrganizationSettingsController", "CategoriesController",
        "UnitsOfMeasureController", "CatalogItemsController", "SuppliersController"
    ];

    private static string[] OtherDomainModuleMarkers(string module) =>
        MasterDataModules.Where(other => other != module).SelectMany(other => new[] { $"Template.Domain.{other}", $"{other}." + "Entities" })
            .Concat(["Template.Domain.Access", "Template.Domain.Products", "Template.Domain.Person", "Template.Domain.Order"])
            .ToArray();

    private static string[] ProviderMarkers() => ["brasilapi.com.br", "olinda.bcb.gov.br", "class BrasilApiClient", "class BcbPtaxClient", "CotacaoMoedaPeriodo"];

    private static string[] HttpClientMarkers() => ["HttpClient", "HttpRequestMessage", "HttpResponseMessage"]; // "System.Net.Http" is an implicit global using

    private static string[] EnrichmentPortLeakMarkers() => ["HttpClient", "HttpResponseMessage", "HttpStatusCode", "JsonElement", "JsonDocument", "Uri", "brasilapi", "olinda"];

    private static string[] UrlLoggingMarkers() => ["{Url}", "{RequestUri}", "RequestUri", "{Path}", "{Cnpj}", "{Cep}", "{PostalCode}", "{TaxId}"];

    private static string[] RoleAuthorizationMarkers() => ["Roles =", "Roles=", "RequireRole(", "IsInRole("];

    private static string[] AuditMutationMarkers() =>
        ["Update(", "Delete(", "Remove(", "ExecuteUpdate", "ExecuteDelete", "Attach("];

    private static string[] SessionCryptoMarkers() =>
        ["RandomNumberGenerator", "HMACSHA256", "HKDF", "SymmetricSecurityKey", "JwtSecurityToken", "SHA256.HashData"];

    /// <summary>Counts <c>[Http*]</c> action attributes whose attribute block has no explicit authorization attribute.</summary>
    private static int ActionsWithoutExplicitAuthorization(string source)
    {
        var lines = source.Split('\n').Select(line => line.Trim()).ToArray();
        var missing = 0;
        for (var index = 0; index < lines.Length; index++)
        {
            if (!lines[index].StartsWith("[Http", StringComparison.Ordinal))
                continue;

            var block = lines.Skip(index).TakeWhile(line => line.StartsWith('[')).ToArray();
            if (!block.Any(line => line.StartsWith("[HasPermission(", StringComparison.Ordinal)
                    || line.StartsWith("[Authorize", StringComparison.Ordinal)
                    || line.StartsWith("[AllowAnonymous]", StringComparison.Ordinal)))
                missing++;
        }

        return missing;
    }

    private static string[] ForbiddenApplicationPackages(IEnumerable<string> packages) => packages
        .Where(package => package.Contains("EntityFrameworkCore", StringComparison.Ordinal)
            || package.Contains("AspNetCore", StringComparison.Ordinal)
            || package.Contains("StackExchangeRedis", StringComparison.Ordinal)
            || package.Contains("RabbitMQ", StringComparison.Ordinal)
            || package.Contains("Elastic", StringComparison.Ordinal)
            || package.Contains("Serilog", StringComparison.Ordinal)
            || package.Equals("Microsoft.Extensions.Hosting.Abstractions", StringComparison.Ordinal))
        .OrderBy(package => package, StringComparer.Ordinal)
        .ToArray();

    private static string[] ApplicationMarkers() =>
    [
        "Microsoft.EntityFrameworkCore", "Microsoft.AspNetCore", "Microsoft.Extensions.Caching",
        "Microsoft.Extensions.Hosting", "RabbitMQ", "Serilog", "HealthChecks", .. SearchVendorMarkers()
    ];

    private static readonly string[] RemovedLoggingPackages =
    [
        "Serilog.Enrichers.ClientInfo", "Serilog.Exceptions", "Serilog.Extensions.Logging"
    ];

    private static string[] ObservabilityFrameworkMarkers() =>
    [
        "Microsoft.Extensions.Logging", "Serilog", "HealthCheck", "Microsoft.AspNetCore", "HttpContext",
        "Microsoft.Extensions.Localization", "IStringLocalizer", "ResourceManager", "CultureInfo"
    ];

    private static string[] LocalizationImplementationMarkers() =>
    [
        "IStringLocalizer", "Microsoft.Extensions.Localization", "Microsoft.AspNetCore.Localization", "ResourceManager",
        "System.Resources", "new RequestCulture", "RequestCultureProvider", "RequestLocalizationOptions", ".resx", "Template.Infrastructure.Resources"
    ];

    private static string[] ApplicationPortMarkers() =>
    [
        "IProductCache", "IOrderEventPublisher", "IPersonSearchIndex", "IProcessedMessageStore", "Template.Application.Interfaces"
    ];

    private const string IdentityProject = "Src/Infrastructure/Template.Infrastructure.Identity/";

    private static string[] IdentityMarkers() =>
    [
        "Microsoft.AspNetCore.Identity", "UserManager<", "SignInManager<", "RoleManager<", "IdentityUser",
        "System.IdentityModel.Tokens.Jwt", "Microsoft.IdentityModel", "JwtSecurityToken", "ClaimsPrincipal",
        "ClaimsIdentity", "System.Security.Claims", "HttpContext", "Microsoft.AspNetCore.Authentication"
    ];

    private static string[] IdentityImplementationMarkers() =>
    [
        "UserManager<", "SignInManager<", "RoleManager<", "IdentityContext", "ApplicationUser", "ApplicationRole"
    ];

    private static string[] JwtConstructionMarkers() =>
    [
        "JwtSecurityToken", "SymmetricSecurityKey", "SigningCredentials", "TokenValidationParameters", "JwtBearerEvents"
    ];

    private static bool IsIdentityPackage(string package) =>
        package.Contains("Identity", StringComparison.Ordinal) || package.Contains("JwtBearer", StringComparison.Ordinal);

    private const string SearchProject = "Src/Infrastructure/Template.Infrastructure.Search/";

    private static string[] SearchVendorMarkers() =>
    [
        "Elastic.", "ElasticsearchClient", "ElasticSearchSettings", "Elasticsearch"
    ];

    private static string[] PortLeakMarkers() =>
    [
        .. SearchVendorMarkers(), "Json", "Transport", "SearchResponse", "Serilog", "HealthCheck", "Uri"
    ];

    private static string[] SearchPortMarkers() =>
    [
        "IPersonSearchIndex", "PersonSearchSettings", "Template.Infrastructure.Search", "Template.Application.Search",
        "Template.Application.Interfaces.Search"
    ];

    private const string MessagingProject = "Src/Infrastructure/Template.Infrastructure.Messaging/";
    private const string WorkerProject = "Src/Workers/Template.Worker/";

    private static string[] MessagingPlumbingMarkers() =>
    [
        "RabbitMQ", "BackgroundService", "IHostedService", "AddHostedService", "ConnectionFactory", "CreateConnection",
        "CreateChannel", "CreateModel", "IModel", "ExchangeDeclare", "QueueDeclare", "QueueBind", "BasicConsume",
        "BasicPublish", "BasicAck", "BasicNack", "RabbitMqSettings"
    ];

    private static string[] SystemClockMarkers() =>
    [
        "DateTime.UtcNow", "DateTime.Now", "DateTime.Today", "DateTimeOffset.UtcNow", "DateTimeOffset.Now"
    ];

    private static string[] PresentationMarkers() =>
    [
        "ApplicationDbContext", "IdentityContext", "IGenericRepository", "IProductRepository",
        "Microsoft.EntityFrameworkCore.DbContext"
    ];

    private static string[] ServiceLocatorMarkers() =>
    [
        "HttpContext.RequestServices", "RequestServices.GetService", "RequestServices.GetRequiredService"
    ];

    private static string[] SourceViolations(string directory, string[] markers) =>
        Directory.GetFiles(Path.Combine(Root, directory), "*.cs", SearchOption.AllDirectories)
            .Where(path => ContainsAny(File.ReadAllText(path), markers))
            .Select(Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static bool ContainsAny(string source, IEnumerable<string> markers) =>
        markers.Any(marker => source.Contains(marker, StringComparison.Ordinal));

    private static bool IsInfrastructureReference(string reference) =>
        reference.Contains("Infrastructure", StringComparison.Ordinal);

    private static string[] References(XDocument project, string elementName) => project
        .Descendants()
        .Where(element => element.Name.LocalName == elementName)
        .Select(element => (string?)element.Attribute("Include"))
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Cast<string>()
        .OrderBy(value => value, StringComparer.Ordinal)
        .ToArray();

    private static string ReadSource(string path) => File.ReadAllText(Path.Combine(Root, path));

    private static XDocument LoadProject(string path) => XDocument.Load(Path.Combine(Root, path));

    private static string[] FilesContaining(string directory, string segment, string pattern) =>
        Directory.GetFiles(Path.Combine(Root, directory), pattern, SearchOption.AllDirectories)
            .Where(path => path.Split(Path.DirectorySeparatorChar).Contains(segment, StringComparer.Ordinal))
            .Select(Relative)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TemplateCleanArchitecture.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
