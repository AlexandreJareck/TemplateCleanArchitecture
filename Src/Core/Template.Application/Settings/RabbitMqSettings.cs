namespace Template.Application.Settings;

public class RabbitMqSettings
{
    public int Port  { get; init; } = 5672;
    public string UserName  { get; init; } = "kalo";
    public string Password { get; init; } = "kalo";
    public string HostName { get; init; } = "rabbitmq";
}
