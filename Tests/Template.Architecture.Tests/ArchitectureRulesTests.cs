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
    public void Application_Has_Only_Named_Temporary_Implementation_Package_Exceptions()
    {
        var project = LoadProject("Src/Core/Template.Application/Template.Application.csproj");
        var actual = ForbiddenApplicationPackages(References(project, "PackageReference"));

        actual.ShouldBe([
            "Elastic.Clients.Elasticsearch", // Phase 10
            "Microsoft.Extensions.Hosting.Abstractions", // Phase 9 hosted consumers
            "RabbitMQ.Client" // Phase 9
        ]);
    }

    [Fact]
    public void Application_Has_Only_Named_Temporary_Source_Exceptions()
    {
        var actual = SourceViolations("Src/Core/Template.Application", ApplicationMarkers());

        actual.ShouldBe([
            "Src/Core/Template.Application/Consumers/OrderConsumer.cs", // Phase 9
            "Src/Core/Template.Application/Services/Elastic/ElasticService.cs", // Phase 10
            "Src/Core/Template.Application/Services/RabbitMq/RabbitMqService.cs" // Phase 9
        ]);
    }

    [Fact]
    public void Presentation_Has_Only_Named_Temporary_Persistence_Exception()
    {
        var actual = SourceViolations("Src/Presentation", PresentationMarkers());

        actual.ShouldBe(["Src/Presentation/Template.WebApi/Program.cs"]); // Phases 7 and 12
    }

    [Fact]
    public void WebApi_Is_The_Only_Project_That_Composes_Infrastructure()
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
            "Tests/Template.Integration.Tests/Template.Integration.Tests.csproj",
            "Tests/Template.PostgreSql.Integration.Tests/Template.PostgreSql.Integration.Tests.csproj"
        ]);
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
        "Microsoft.Extensions.Hosting", "RabbitMQ", "Elastic.Clients", "Serilog"
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
