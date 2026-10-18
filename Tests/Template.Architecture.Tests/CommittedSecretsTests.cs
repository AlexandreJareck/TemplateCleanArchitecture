using System.Text.Json;
using System.Text.RegularExpressions;
using Shouldly;

namespace Template.Architecture.Tests;

/// <summary>
/// Phase 22: committed configuration carries no secret values. The JWT signing key (<c>JwtSettings:Key</c>), passwords
/// and connection-string credentials come only from non-committed sources (user secrets, environment variables, the
/// git-ignored <c>.env</c>). Findings name the file and key, never the value.
/// </summary>
public sealed partial class CommittedSecretsTests
{
    private const string SyntheticValue = "synthetic-mutation-value-0123456789abcdef";
    private static readonly string Root = FindRepositoryRoot();

    [Fact]
    public void Committed_Settings_Files_Carry_No_Secret_Values()
    {
        var files = SettingsFiles();

        files.ShouldContain("Src/Presentation/Template.WebApi/appsettings.json");
        files.ShouldContain("Src/Presentation/Template.WebApi/appsettings.Production.json");
        files.SelectMany(file => FindJsonSecrets(file, File.ReadAllText(Path.Combine(Root, file)))).ShouldBeEmpty();
    }

    [Fact]
    public void Compose_Files_Pass_The_Signing_Key_Only_Through_Interpolation()
    {
        var files = Directory.EnumerateFiles(Root, "docker-compose*.yml").Append(Path.Combine(Root, ".env.example"));

        files.SelectMany(file => FindComposeSecrets(Relative(file), File.ReadAllText(file))).ShouldBeEmpty();
        File.ReadAllText(Path.Combine(Root, "docker-compose.yml")).ShouldContain("JwtSettings__Key=${JwtSettings__Key}");
    }

    // Controlled mutations: each forbidden shape is detected and the finding never echoes the value.
    [Theory]
    [InlineData("""{ "JwtSettings": { "Key": "VALUE", "Issuer": "CoreIdentity" } }""", "JwtSettings:Key")]
    [InlineData("""{ "RabbitMqSettings": { "UserName": "user", "Password": "VALUE" } }""", "RabbitMqSettings:Password")]
    [InlineData("""{ "ConnectionStrings": { "DefaultConnection": "Host=db;Username=u;Password=VALUE;Database=d" } }""", "ConnectionStrings:DefaultConnection")]
    [InlineData("""{ "ConnectionStrings": { "RedisConnection": "localhost:6379,password=VALUE" } }""", "ConnectionStrings:RedisConnection")]
    [InlineData("""{ "Outer": { "Client": { "ClientSecret": "VALUE" } } }""", "Outer:Client:ClientSecret")]
    [InlineData("""{ "Items": [ { "ApiKey": "VALUE" } ] }""", "Items:0:ApiKey")]
    public void Json_Secret_Mutation_Is_Detected_Without_Echoing_The_Value(string json, string expectedKey)
    {
        var findings = FindJsonSecrets("mutated.json", json.Replace("VALUE", SyntheticValue, StringComparison.Ordinal));

        findings.ShouldHaveSingleItem().ShouldBe($"mutated.json: {expectedKey}");
        findings.ShouldAllBe(finding => !finding.Contains(SyntheticValue));
    }

    [Theory]
    [InlineData("""{ "JwtSettings": { "Key": "", "Issuer": "CoreIdentity", "DurationInMinutes": 15 } }""")]
    [InlineData("""{ "IdentitySettings": { "PasswordRequireDigit": true, "PasswordRequiredLength": 5 } }""")]
    [InlineData("""{ "ConnectionStrings": { "DefaultConnection": "Host=db;Port=5432;Database=d;SearchPath=public" } }""")]
    [InlineData("""{ "ElasticSearchSettings": { "DefaultIndex": "persons", "Name": "elasticsearch" } }""")]
    public void Json_Without_Secret_Values_Is_Accepted(string json) =>
        FindJsonSecrets("clean.json", json).ShouldBeEmpty();

    [Theory]
    [InlineData("      - JwtSettings__Key=VALUE", true)]
    [InlineData("      JwtSettings__Key: VALUE", true)]
    [InlineData("JwtSettings__Key=VALUE", true)]
    [InlineData("      - JwtSettings__Key=${JwtSettings__Key}", false)]
    [InlineData("JwtSettings__Key=CHANGE_ME", false)]
    public void Compose_Signing_Key_Mutation_Is_Detected_Without_Echoing_The_Value(string line, bool expected)
    {
        var findings = FindComposeSecrets("mutated.yml", line.Replace("VALUE", SyntheticValue, StringComparison.Ordinal));

        (findings.Length == 1).ShouldBe(expected);
        findings.ShouldAllBe(finding => !finding.Contains(SyntheticValue));
    }

    internal static string[] FindJsonSecrets(string file, string json)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        });
        var findings = new List<string>();
        Walk(document.RootElement, string.Empty, string.Empty, findings);
        return [.. findings.Select(path => $"{file}: {path}")];
    }

    internal static string[] FindComposeSecrets(string file, string text) =>
    [
        .. text.Split('\n')
            .Select((line, index) => (Line: line.Trim(), Number: index + 1))
            .Where(entry => SigningKeyAssignment().Match(entry.Line) is { Success: true } match
                && match.Groups["value"].Value.Trim() is { Length: > 0 } value
                && value != "${JwtSettings__Key}" && value != "CHANGE_ME")
            .Select(entry => $"{file}:{entry.Number}: JwtSettings__Key")
    ];

    private static void Walk(JsonElement element, string path, string name, List<string> findings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                    Walk(property.Value, Join(path, property.Name), property.Name, findings);
                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                    Walk(item, Join(path, (index++).ToString(System.Globalization.CultureInfo.InvariantCulture)), name, findings);
                break;
            case JsonValueKind.String:
                var value = element.GetString() ?? string.Empty;
                if ((SecretName().IsMatch(name) && value.Length > 0) || ConnectionStringCredential().IsMatch(value))
                    findings.Add(path);
                break;
        }
    }

    private static string Join(string path, string segment) => path.Length == 0 ? segment : $"{path}:{segment}";

    private static string[] SettingsFiles() =>
    [
        .. new[] { "Src", "Tests" }
            .SelectMany(directory => Directory.EnumerateFiles(Path.Combine(Root, directory), "*.json", SearchOption.AllDirectories))
            .Select(Relative)
            .Where(path => !path.Contains("/bin/", StringComparison.Ordinal) && !path.Contains("/obj/", StringComparison.Ordinal))
            .Where(path => Path.GetFileName(path).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith("/Properties/launchSettings.json", StringComparison.Ordinal))
            .OrderBy(path => path, StringComparer.Ordinal)
    ];

    /// <summary>A property whose name ends in Key, Password, Pwd, Secret or Token (case-insensitive).</summary>
    [GeneratedRegex("(key|password|pwd|secret|token)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretName();

    /// <summary>A non-empty password inside a connection string (Npgsql <c>;</c> or StackExchange.Redis <c>,</c> style).</summary>
    [GeneratedRegex(@"(^|[;,])\s*(password|pwd)\s*=\s*[^;,\s]", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ConnectionStringCredential();

    [GeneratedRegex(@"^(-\s*)?JwtSettings__Key\s*[:=](?<value>.*)$", RegexOptions.CultureInvariant)]
    private static partial Regex SigningKeyAssignment();

    private static string Relative(string path) => Path.GetRelativePath(Root, path).Replace('\\', '/');

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "TemplateCleanArchitecture.sln")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Repository root was not found.");
    }
}
