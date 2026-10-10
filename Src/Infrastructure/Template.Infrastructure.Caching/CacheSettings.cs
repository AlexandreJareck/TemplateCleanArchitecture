namespace Template.Infrastructure.Caching;

public sealed class CacheSettings
{
    public const string SectionName = nameof(CacheSettings);

    public bool Enabled { get; set; } = true;

    public TimeSpan AbsoluteExpiration { get; set; } = TimeSpan.FromMinutes(10);

    public TimeSpan SlidingExpiration { get; set; } = TimeSpan.FromMinutes(2);

    public TimeSpan OperationTimeout { get; set; } = TimeSpan.FromSeconds(1);

    public static bool IsValid(CacheSettings settings) =>
        settings.AbsoluteExpiration > TimeSpan.Zero
        && settings.SlidingExpiration > TimeSpan.Zero
        && settings.SlidingExpiration <= settings.AbsoluteExpiration
        && settings.OperationTimeout > TimeSpan.Zero;
}
