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
                "Src/Workers/Template.Worker/WorkerRequestContext.cs",
                "Src/Functions/Template.WorkflowFunctions/Hosting/WorkflowHosting.cs"], ignoreOrder: true);
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
            "Src/Functions/Template.WorkflowFunctions/Template.WorkflowFunctions.csproj",
            "Src/Presentation/Template.WebApi/Template.WebApi.csproj",
            "Src/Workers/Template.Worker/Template.Worker.csproj",
            "Tests/Template.Azurite.Integration.Tests/Template.Azurite.Integration.Tests.csproj",
            "Tests/Template.Elasticsearch.Integration.Tests/Template.Elasticsearch.Integration.Tests.csproj",
            "Tests/Template.Integration.Tests/Template.Integration.Tests.csproj",
            "Tests/Template.PostgreSql.Integration.Tests/Template.PostgreSql.Integration.Tests.csproj",
            "Tests/Template.RabbitMq.Integration.Tests/Template.RabbitMq.Integration.Tests.csproj",
            "Tests/Template.Redis.Integration.Tests/Template.Redis.Integration.Tests.csproj",
            "Tests/Template.WorkflowFunctions.Integration.Tests/Template.WorkflowFunctions.Integration.Tests.csproj"
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
            // Phase 30: blob storage and the PDF/XLSX renderers run only in the Worker.
            @"..\..\Infrastructure\Template.Infrastructure.Files\Template.Infrastructure.Files.csproj",
            @"..\..\Infrastructure\Template.Infrastructure.Messaging\Template.Infrastructure.Messaging.csproj",
            @"..\..\Infrastructure\Template.Infrastructure.Persistence\Template.Infrastructure.Persistence.csproj",
            @"..\..\Infrastructure\Template.Infrastructure.Reporting\Template.Infrastructure.Reporting.csproj",
            // Phase 31: the approval-start consumer and the reconciliation sweep call the WorkflowFunctions client.
            @"..\..\Infrastructure\Template.Infrastructure.Workflow\Template.Infrastructure.Workflow.csproj"
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

        controllers.Length.ShouldBe(21); // Phase 24: 3; Phase 25: +9 master-data, enrichment and user-lookup; Phase 27: +3 budget and requisition controllers; Phase 29: +1 requests for quotation; Phase 30: +1 report jobs; Phase 31: +2 approval instances and policies; Phase 32: +2 purchase orders and receipts
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

    private static readonly string[] MasterDataModules = ["Organization", "Suppliers", "Catalog", "Budgeting", "Requisitions", "Sourcing", "Files", "Reporting", "Approvals", "Purchasing", "Receiving"];

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
            .ShouldBe([$"{IntegrationsProject}Template.Infrastructure.Integrations.csproj",
                // Phase 31 (ADR-0003 §5): the internal WorkflowFunctions client; it calls no external provider.
                "Src/Infrastructure/Template.Infrastructure.Workflow/Template.Infrastructure.Workflow.csproj"]);
        SourceViolations("Src/Infrastructure/Template.Infrastructure.Workflow", ProviderMarkers()).ShouldBeEmpty();
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
        // Phase 31: the approval outcome commits (BUD-010, APR-013) or releases (APR-009, APR-011) in the same commit as
        // the instance. Phase 32: the purchase-order cancel (PUR-008) and short close (PUR-010) release or return the
        // commitment in the order's commit. No controller or other use case reserves, commits or releases funds directly.
        SourceViolations("Src/Core/Template.Application", ReservationMutationMarkers())
            .ShouldBe(["Src/Core/Template.Application/Features/Approvals/ApprovalOutcomes.cs",
                "Src/Core/Template.Application/Features/Purchasing/PurchaseOrderUseCases.cs",
                "Src/Core/Template.Application/Features/Requisitions/RequisitionTransitionUseCases.cs"]);
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

    [Fact]
    public void Phase30_Rendering_And_Storage_Vendors_Stay_In_Their_Adapters()
    {
        // ADR-0004 / FIL-001: Core never sees Playwright, ClosedXML, Open XML or Azure types; the browser and the
        // spreadsheet engine live only in Infrastructure.Reporting (composed by the Worker), blob storage only in Files.
        SourceViolations("Src/Core", ReportingVendorMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core", BlobVendorMarkers()).ShouldBeEmpty();
        foreach (var project in Directory.GetDirectories(Path.Combine(Root, "Src/Infrastructure")).Select(Relative))
        {
            if (!project.EndsWith("Template.Infrastructure.Reporting", StringComparison.Ordinal))
                SourceViolations(project, ReportingVendorMarkers()).ShouldBeEmpty(project);
            if (!project.EndsWith("Template.Infrastructure.Files", StringComparison.Ordinal))
                SourceViolations(project, BlobVendorMarkers()).ShouldBeEmpty(project);
        }

        SourceViolations("Src/Presentation", ReportingVendorMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Presentation", BlobVendorMarkers()).ShouldBeEmpty();
        References(LoadProject("Src/Presentation/Template.WebApi/Template.WebApi.csproj"), "ProjectReference")
            .ShouldNotContain(reference => reference.Contains("Template.Infrastructure.Reporting", StringComparison.Ordinal));
        foreach (var project in new[] { "Src/Core/Template.Application/Template.Application.csproj", "Src/Core/Template.Domain/Template.Domain.csproj" })
            References(LoadProject(project), "PackageReference").ShouldNotContain(package =>
                package.StartsWith("Azure.", StringComparison.Ordinal) || package == "ClosedXML" || package == "Microsoft.Playwright");
    }

    [Fact]
    public void Phase30_Files_Domain_Holds_Metadata_Only_And_Reports_Accept_No_Templates()
    {
        // FIL-001: no bytes, streams or storage keys in the Files/Reporting domain; RPT-008: the HTTP request carries no
        // template, markup or script and the HTML template encodes every value.
        SourceViolations("Src/Core/Template.Domain/Files", FileBytesMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain/Reporting", FileBytesMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain/Files", PublicSetterMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain/Reporting", PublicSetterMarkers()).ShouldBeEmpty();
        ContainsAny(ReadSource("Src/Presentation/Template.WebApi/Controllers/v2/ReportingRequests.cs"), TemplateInputMarkers()).ShouldBeFalse();
        var template = ReadSource("Src/Infrastructure/Template.Infrastructure.Reporting/Pdf/ReportHtmlTemplate.cs");
        template.ShouldContain("WebUtility.HtmlEncode");
        template.ShouldContain("default-src 'none'");
        ReadSource("Src/Infrastructure/Template.Infrastructure.Reporting/Pdf/PlaywrightPdfRenderer.cs").ShouldContain("JavaScriptEnabled = false");
    }

    [Fact]
    public void Phase30_Controlled_Mutations_Are_Detected()
    {
        ContainsAny("using ClosedXML.Excel;", ReportingVendorMarkers()).ShouldBeTrue();
        ContainsAny("using Microsoft.Playwright;", ReportingVendorMarkers()).ShouldBeTrue();
        ContainsAny("using Azure.Storage.Blobs;", BlobVendorMarkers()).ShouldBeTrue();
        ContainsAny("using Template.Application.Interfaces.Files;", BlobVendorMarkers()).ShouldBeFalse();
        ContainsAny("public byte[] Content { get; private set; }", FileBytesMarkers()).ShouldBeTrue();
        ContainsAny("public string StorageKey { get; private set; }", FileBytesMarkers()).ShouldBeTrue();
        ContainsAny("public string Sha256 { get; private set; }", FileBytesMarkers()).ShouldBeFalse();
        ContainsAny("public sealed record RequestReportBody(string? ReportType, string? Markup);", TemplateInputMarkers()).ShouldBeTrue();
        ContainsAny("using Template.Domain.Reporting;", OtherDomainModuleMarkers("Files")).ShouldBeTrue();
        ContainsAny("using Template.Domain.Requisitions;", OtherDomainModuleMarkers("Reporting")).ShouldBeTrue();
    }

    [Fact]
    public void Phase31_Functions_And_Durable_Types_Stay_In_The_WorkflowFunctions_Host()
    {
        // ADR-0003 §5: Domain, Application, Infrastructure, WebApi and Worker never reference Azure Functions or Durable
        // types; they reach the orchestration only through IApprovalWorkflowClient.
        foreach (var directory in new[] { "Src/Core", "Src/Infrastructure", "Src/Presentation", "Src/Workers" })
            SourceViolations(directory, DurableVendorMarkers()).ShouldBeEmpty(directory);
        SourceViolations("Src/Functions", DurableVendorMarkers()).ShouldNotBeEmpty();
        foreach (var project in Directory.GetFiles(Path.Combine(Root, "Src"), "*.csproj", SearchOption.AllDirectories).Select(Relative)
                     .Where(project => !project.StartsWith("Src/Functions/", StringComparison.Ordinal)))
            References(LoadProject(project), "PackageReference").ShouldNotContain(package =>
                package.StartsWith("Microsoft.Azure.Functions", StringComparison.Ordinal) || package.StartsWith("Microsoft.DurableTask", StringComparison.Ordinal),
                project);
        References(LoadProject("Src/Functions/Template.WorkflowFunctions/Template.WorkflowFunctions.csproj"), "ProjectReference")
            .ShouldBe([@"..\..\Core\Template.Application\Template.Application.csproj",
                @"..\..\Infrastructure\Template.Infrastructure.Identity\Template.Infrastructure.Identity.csproj",
                @"..\..\Infrastructure\Template.Infrastructure.Persistence\Template.Infrastructure.Persistence.csproj"]);
    }

    [Fact]
    public void Phase31_Orchestrator_Is_Deterministic_And_Holds_No_Business_Rule()
    {
        // APR-016 -a: the orchestration references no Domain policy, no infrastructure, no ambient clock, randomness,
        // I/O, service location or non-replay-safe logger; it only coordinates activities, timers and the signal event.
        var orchestration = Directory.GetFiles(Path.Combine(Root, "Src/Functions/Template.WorkflowFunctions/Orchestration"), "*.cs").Select(Relative).ToArray();
        orchestration.ShouldNotBeEmpty();
        foreach (var path in orchestration)
            ContainsAny(ReadSource(path), OrchestratorNondeterminismMarkers()).ShouldBeFalse(path);
        ReadSource("Src/Functions/Template.WorkflowFunctions/Orchestration/ApprovalOrchestration.cs").ShouldContain("CreateReplaySafeLogger");
    }

    [Fact]
    public void Phase31_Approval_Domain_Is_Encapsulated_And_The_Browser_Never_Reaches_The_Workflow_Host()
    {
        SourceViolations("Src/Core/Template.Domain/Approvals", PublicSetterMarkers()).ShouldBeEmpty();
        SourceViolations("Src/Core/Template.Domain/Approvals", OtherDomainModuleMarkers("Approvals")).ShouldBeEmpty();
        var client = Path.Combine(Root, "Client/Template.WebApp/src");
        if (Directory.Exists(client))
            Directory.GetFiles(client, "*.*", SearchOption.AllDirectories)
                .Where(path => path.EndsWith(".ts", StringComparison.Ordinal) || path.EndsWith(".json", StringComparison.Ordinal))
                .Where(path => ContainsAny(File.ReadAllText(path), WorkflowHostMarkers()))
                .Select(Relative)
                .ShouldBeEmpty();
        foreach (var settings in Directory.GetFiles(Path.Combine(Root, "Client/Template.WebApp/public"), "*.json", SearchOption.AllDirectories))
            ContainsAny(File.ReadAllText(settings), WorkflowHostMarkers()).ShouldBeFalse(Relative(settings));
    }

    [Fact]
    public void Phase31_Controlled_Mutations_Are_Detected()
    {
        ContainsAny("using Microsoft.DurableTask;", DurableVendorMarkers()).ShouldBeTrue();
        ContainsAny("[DurableClient] DurableTaskClient client", DurableVendorMarkers()).ShouldBeTrue();
        ContainsAny("using Template.Application.Workflow;", DurableVendorMarkers()).ShouldBeFalse();
        ContainsAny("var now = DateTime.UtcNow;", OrchestratorNondeterminismMarkers()).ShouldBeTrue();
        ContainsAny("var id = Guid.NewGuid();", OrchestratorNondeterminismMarkers()).ShouldBeTrue();
        ContainsAny("await Task.Delay(1000);", OrchestratorNondeterminismMarkers()).ShouldBeTrue();
        ContainsAny("if (amount > policy.Levels[1].MinimumAmountExclusive)", OrchestratorNondeterminismMarkers()).ShouldBeTrue();
        ContainsAny("using Template.Domain.Approvals;", OrchestratorNondeterminismMarkers()).ShouldBeTrue();
        ContainsAny("var timer = context.CreateTimer(fireAt, token);", OrchestratorNondeterminismMarkers()).ShouldBeFalse();
        ContainsAny("fetch('/api/approval-workflows/' + id)", WorkflowHostMarkers()).ShouldBeTrue();
        ContainsAny("headers: { 'X-Workflow-Key': key }", WorkflowHostMarkers()).ShouldBeTrue();
        ContainsAny("this.http.get('/api/v2/approval-instances')", WorkflowHostMarkers()).ShouldBeFalse();
        ContainsAny("using Template.Domain.Sourcing;", OtherDomainModuleMarkers("Approvals")).ShouldBeTrue();
    }

    [Fact]
    public void Phase32_Purchasing_And_Receiving_Are_Encapsulated_Append_Only_And_Gapless()
    {
        foreach (var module in new[] { "Purchasing", "Receiving" })
        {
            SourceViolations($"Src/Core/Template.Domain/{module}", PublicSetterMarkers()).ShouldBeEmpty(module);
            SourceViolations($"Src/Core/Template.Domain/{module}", OtherDomainModuleMarkers(module)).ShouldBeEmpty(module);
        }

        // RCV-006: posted receipts are never edited or deleted; PUR records are never deleted (NFR retention).
        ContainsAny(ReadSource("Src/Presentation/Template.WebApi/Controllers/v2/ReceiptsController.cs"), ReceiptMutationEndpointMarkers()).ShouldBeFalse();
        ContainsAny(ReadSource("Src/Presentation/Template.WebApi/Controllers/v2/PurchaseOrdersController.cs"), ["[HttpDelete"]).ShouldBeFalse();
        ContainsAny(ReadSource("Src/Core/Template.Application/Interfaces/Purchasing/IPurchasingPorts.cs"), ["void Remove(", "void Delete("]).ShouldBeFalse();
        // SP-2: purchase-order numbers come from the per-year counter inside the issue transaction, never from a sequence.
        ContainsAny(ReadSource("Src/Infrastructure/Template.Infrastructure.Persistence/Repositories/PurchasingRepositories.cs"), SequenceMarkers()).ShouldBeFalse();
        // The API never exposes storage or broker details (handoff): views carry API paths and IDs only.
        ContainsAny(ReadSource("Src/Core/Template.Application/Features/Purchasing/PurchasingViews.cs"), InfrastructureLeakMarkers()).ShouldBeFalse();
    }

    [Fact]
    public void Phase32_Controlled_Mutations_Are_Detected()
    {
        ContainsAny("[HttpPut(\"{id:guid}\")]", ReceiptMutationEndpointMarkers()).ShouldBeTrue();
        ContainsAny("[HttpDelete(\"{id:guid}\")]", ReceiptMutationEndpointMarkers()).ShouldBeTrue();
        ContainsAny("[HttpPost(\"{id:guid}/corrections\")]", ReceiptMutationEndpointMarkers()).ShouldBeFalse();
        ContainsAny("SELECT nextval('purchasing_order_numbers')", SequenceMarkers()).ShouldBeTrue();
        ContainsAny("CREATE SEQUENCE po_numbers", SequenceMarkers()).ShouldBeTrue();
        ContainsAny("UPDATE \"PurchasingOrderNumberCounters\" SET \"LastValue\" = \"LastValue\" + 1 RETURNING", SequenceMarkers()).ShouldBeFalse();
        ContainsAny("string BlobUri,", InfrastructureLeakMarkers()).ShouldBeTrue();
        ContainsAny("string RoutingKey,", InfrastructureLeakMarkers()).ShouldBeTrue();
        ContainsAny("string? ContentUrl);", InfrastructureLeakMarkers()).ShouldBeFalse();
        ContainsAny("using Template.Domain.Purchasing;", OtherDomainModuleMarkers("Receiving")).ShouldBeTrue();
        ContainsAny("public decimal AcceptedQuantity { get; set; }", PublicSetterMarkers()).ShouldBeTrue();
    }

    private static string[] ReceiptMutationEndpointMarkers() => ["[HttpPut", "[HttpPatch", "[HttpDelete"];

    private static string[] SequenceMarkers() => ["nextval", "NEXTVAL", "CREATE SEQUENCE", "HasSequence", "UseHiLo", "UseSequence"];

    private static string[] InfrastructureLeakMarkers() => ["Blob", "Azurite", "ContainerName", "StorageKey", "RabbitMq", "RoutingKey", "Exchange", "QueueName"];

    private static string[] DurableVendorMarkers() =>
        ["using Microsoft.DurableTask", "using Microsoft.Azure.Functions", "DurableTaskClient", "TaskOrchestrationContext", "[OrchestrationTrigger]", "[ActivityTrigger]"];

    private static string[] OrchestratorNondeterminismMarkers() =>
    [
        .. SystemClockMarkers(), "Guid.NewGuid", "new Random", "Random.Shared", "Thread.Sleep", "Task.Delay", "Task.Run", "HttpClient", "File.",
        "Directory.", "Environment.", "IServiceProvider", "GetRequiredService", "ILogger<", "Template.Domain", "Template.Infrastructure",
        "DbContext", "MinimumAmount", "Threshold", "ApprovalPolicy"
    ];

    private static string[] WorkflowHostMarkers() => ["approval-workflows", "X-Workflow-Key", "runtime/webhooks/durabletask", "ApprovalWorkflowSettings"];

    private static string[] ReportingVendorMarkers() => ["using ClosedXML", "using Microsoft.Playwright", "using DocumentFormat.OpenXml", "ClosedXML.Excel.", "Microsoft.Playwright."];

    private static string[] BlobVendorMarkers() => ["using Azure.Storage", "using Azure;", "Azure.Storage.Blobs."];

    private static string[] FileBytesMarkers() => ["byte[]", "Stream ", "StorageKey", "BlobName", "ContainerName"];

    private static string[] TemplateInputMarkers() => ["Html", "Script", "Css", "Markup", "string? Template"];

    private static string[] AwardAdjustmentMarkers() => [".AdjustForAward("];

    private static string[] BrowserRankingMarkers() => ["* rate", "*rate", "normalizedTotal -", "rank =", "rank++"];

    private static string[] RawSqlMarkers() => ["ExecuteSqlRaw", "ExecuteSqlInterpolated", "FromSqlRaw", "FromSqlInterpolated", "SqlQuery<", "SqlQueryRaw<"];

    private static string[] ReservationMutationMarkers() => [".Reserve(", ".Release(", ".Commit(", ".Uncommit(", ".ReleasePartially("];

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
