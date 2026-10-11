using System.Reflection;
using System.Runtime.CompilerServices;
using Shouldly;

namespace Template.Functional.Tests.Observability;

/// <summary>
/// The 9.x client and the Serilog sink share one <c>Elastic.Transport</c> in the WebApi graph. The sink's ingest
/// assemblies were compiled against an older transport than the client requires, which is the mismatch that broke
/// the previous health-check package at runtime (TypeLoadException). These tests load every type and JIT-compile
/// every non-generic method body of those assemblies against the resolved transport, so a removed or changed member
/// fails here instead of at the first export or bootstrap in production.
/// </summary>
public sealed class ElasticDependencyCompatibilityTests
{
    private static readonly string[] SinkAssemblies =
    [
        "Elastic.Channels",
        "Elastic.Ingest.Transport",
        "Elastic.Ingest.Elasticsearch",
        "Elastic.Ingest.Elasticsearch.CommonSchema",
        "Elastic.CommonSchema",
        "Elastic.CommonSchema.Serilog",
        "Elastic.Serilog.Sinks"
    ];

    [Fact]
    public void ResolvedGraph_HasOneTransport_AndThe9xClientAndSink()
    {
        var transport = Assembly.Load("Elastic.Transport").GetName().Version!;
        var client = Assembly.Load("Elastic.Clients.Elasticsearch").GetName().Version!;
        var sink = Assembly.Load("Elastic.Serilog.Sinks").GetName().Version!;

        client.Major.ShouldBe(9);
        sink.Major.ShouldBe(9);
        transport.Major.ShouldBeGreaterThanOrEqualTo(1);
        AppDomain.CurrentDomain.GetAssemblies().Count(assembly => assembly.GetName().Name == "Elastic.Transport").ShouldBe(1);
    }

    [Theory]
    [MemberData(nameof(Assemblies))]
    public void SinkAssembly_BindsAgainstTheResolvedTransport(string assemblyName)
    {
        var assembly = Assembly.Load(assemblyName);
        var failures = new List<string>();

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            types = error.Types.OfType<Type>().ToArray();
            failures.AddRange(error.LoaderExceptions.OfType<Exception>().Select(loader => loader.Message));
        }

        foreach (var type in types.Where(type => !type.ContainsGenericParameters))
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (var method in methods.Where(method => !method.IsAbstract && !method.ContainsGenericParameters && method.GetMethodBody() is not null))
            {
                try
                {
                    RuntimeHelpers.PrepareMethod(method.MethodHandle);
                }
                catch (Exception error) when (error is MissingMemberException or TypeLoadException or BadImageFormatException or FileLoadException)
                {
                    failures.Add($"{type.FullName}.{method.Name}: {error.GetType().Name}: {error.Message}");
                }
            }
        }

        failures.ShouldBeEmpty();
    }

    public static TheoryData<string> Assemblies() => new(SinkAssemblies);
}
