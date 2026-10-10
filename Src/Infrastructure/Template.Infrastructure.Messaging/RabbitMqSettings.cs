namespace Template.Infrastructure.Messaging;

/// <summary>
/// Broker options bound from the <c>RabbitMqSettings</c> section. Credentials have no default in code: supply them
/// through appsettings, user secrets or environment variables.
/// </summary>
public sealed class RabbitMqSettings
{
    public const string SectionName = nameof(RabbitMqSettings);

    /// <summary>Master switch. When false, publishing fails with a MessagePublishException and no consumer starts.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>When false, the consumers stay stopped while publishing keeps working.</summary>
    public bool ConsumersEnabled { get; set; } = true;

    public string HostName { get; set; } = "rabbitmq";

    public int Port { get; set; } = 5672;

    public string UserName { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    /// <summary>Upper bound for opening a TCP/AMQP connection.</summary>
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Upper bound for one publish, including connecting and waiting for the broker confirmation.</summary>
    public TimeSpan PublishTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>First delay between consumer connection attempts; it doubles up to <see cref="MaxReconnectDelay"/>.</summary>
    public TimeSpan ReconnectDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan MaxReconnectDelay { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Processing attempts per message before it is dead-lettered.</summary>
    public int MaxDeliveryAttempts { get; set; } = 3;

    /// <summary>Delay before a failed message is requeued; multiplied by the attempt number.</summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);

    /// <summary>Unacknowledged messages per consumer.</summary>
    public ushort PrefetchCount { get; set; } = 1;

    public static bool IsValid(RabbitMqSettings settings) =>
        !settings.Enabled
        || (!string.IsNullOrWhiteSpace(settings.HostName)
            && settings.Port is > 0 and <= 65535
            && !string.IsNullOrEmpty(settings.UserName)
            && !string.IsNullOrEmpty(settings.Password)
            && settings.ConnectionTimeout > TimeSpan.Zero
            && settings.PublishTimeout > TimeSpan.Zero
            && settings.ReconnectDelay > TimeSpan.Zero
            && settings.MaxReconnectDelay >= settings.ReconnectDelay
            && settings.MaxDeliveryAttempts >= 1
            && settings.RetryDelay >= TimeSpan.Zero
            && settings.PrefetchCount >= 1);
}
