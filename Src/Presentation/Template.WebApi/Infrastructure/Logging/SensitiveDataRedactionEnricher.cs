using System.Text.RegularExpressions;
using Serilog.Core;
using Serilog.Events;

namespace Template.WebApi.Infrastructure.Logging;

/// <summary>
/// Last line of defense of the redaction policy, applied to every event (application and framework): a property
/// whose name denotes a secret is replaced by <see cref="Redacted"/>, and bearer tokens, JWTs and connection-string
/// passwords inside string values are masked. Explicit log calls must still never pass secrets or payloads; this only
/// catches mistakes and framework properties such as <c>QueryString</c>. It never mutates application data.
/// </summary>
public sealed partial class SensitiveDataRedactionEnricher : ILogEventEnricher
{
    public const string Redacted = "[REDACTED]";

    private static readonly string[] SensitiveNameParts =
    [
        "password", "passwd", "pwd", "token", "jwt", "authorization", "cookie", "secret", "apikey", "api_key",
        "connectionstring", "securitystamp", "signingkey", "privatekey", "querystring", "credential"
    ];

    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        foreach (var (name, value) in logEvent.Properties.ToArray())
        {
            var redacted = Redact(name, value);
            if (!ReferenceEquals(redacted, value))
                logEvent.AddOrUpdateProperty(new LogEventProperty(name, redacted));
        }
    }

    public static bool IsSensitiveName(string name)
    {
        var normalized = name.Replace("-", string.Empty).Replace(".", string.Empty);
        return SensitiveNameParts.Any(part => normalized.Contains(part, StringComparison.OrdinalIgnoreCase));
    }

    public static string RedactText(string text)
    {
        text = BearerPattern().Replace(text, "Bearer " + Redacted);
        text = JwtPattern().Replace(text, Redacted);
        return SecretAssignmentPattern().Replace(text, match => match.Groups["key"].Value + "=" + Redacted);
    }

    private static LogEventPropertyValue Redact(string name, LogEventPropertyValue value)
    {
        if (IsSensitiveName(name))
            return new ScalarValue(Redacted);

        switch (value)
        {
            case ScalarValue { Value: string text }:
                var redacted = RedactText(text);
                return redacted == text ? value : new ScalarValue(redacted);
            case StructureValue structure:
                var properties = structure.Properties.Select(property => new LogEventProperty(property.Name, Redact(property.Name, property.Value))).ToArray();
                return properties.Zip(structure.Properties).All(pair => ReferenceEquals(pair.First.Value, pair.Second.Value))
                    ? value
                    : new StructureValue(properties, structure.TypeTag);
            case SequenceValue sequence:
                var elements = sequence.Elements.Select(element => Redact(string.Empty, element)).ToArray();
                return elements.Zip(sequence.Elements).All(pair => ReferenceEquals(pair.First, pair.Second))
                    ? value
                    : new SequenceValue(elements);
            case DictionaryValue dictionary:
                var entries = dictionary.Elements
                    .Select(entry => new KeyValuePair<ScalarValue, LogEventPropertyValue>(
                        entry.Key,
                        Redact(entry.Key.Value?.ToString() ?? string.Empty, entry.Value)))
                    .ToArray();
                return entries.Zip(dictionary.Elements).All(pair => ReferenceEquals(pair.First.Value, pair.Second.Value))
                    ? value
                    : new DictionaryValue(entries);
            default:
                return value;
        }
    }

    [GeneratedRegex(@"Bearer\s+[A-Za-z0-9\-._~+/]+=*", RegexOptions.IgnoreCase)]
    private static partial Regex BearerPattern();

    [GeneratedRegex(@"eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]*")]
    private static partial Regex JwtPattern();

    [GeneratedRegex(@"(?<key>password|pwd|user\s*id|uid|username|api[_-]?key|access[_-]?token|token)\s*=\s*[^;&\s]*", RegexOptions.IgnoreCase)]
    private static partial Regex SecretAssignmentPattern();
}
