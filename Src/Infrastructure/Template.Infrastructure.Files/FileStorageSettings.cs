namespace Template.Infrastructure.Files;

/// <summary>
/// Blob storage options (D23-7). The connection string is a secret read from <c>ConnectionStrings:BlobStorage</c>
/// (user secrets, the git-ignored Compose <c>.env</c> or a secret store) and never logged or committed.
/// </summary>
public sealed class FileStorageSettings
{
    public const string SectionName = nameof(FileStorageSettings);
    public const string ConnectionStringName = "BlobStorage";

    /// <summary>When false (or in Test) the adapter is replaced: in-memory in Test, an always-unavailable store elsewhere.</summary>
    public bool Enabled { get; set; }

    /// <summary>Private container (no anonymous access) holding every stored file.</summary>
    public string ContainerName { get; set; } = "template-files";

    /// <summary>Per-operation network timeout, including retries.</summary>
    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Transport retries of transient failures (0–5).</summary>
    public int MaxRetries { get; set; } = 2;

    public static bool IsValid(FileStorageSettings settings) =>
        !string.IsNullOrWhiteSpace(settings.ContainerName)
        && settings.ContainerName.Length is >= 3 and <= 63
        && settings.ContainerName.All(character => character is >= 'a' and <= 'z' or >= '0' and <= '9' or '-')
        && !settings.ContainerName.StartsWith('-') && !settings.ContainerName.EndsWith('-') && !settings.ContainerName.Contains("--")
        && settings.OperationTimeout > TimeSpan.Zero && settings.OperationTimeout <= TimeSpan.FromMinutes(10)
        && settings.MaxRetries is >= 0 and <= 5;
}
