using Template.Application.Consumers;
using Template.Domain.Order;

namespace Template.Application.Services.RabbitMq;

public interface IRabbitMqService
{
    public void PublishOrderFanout(Order order);
    public void PublishOrderDirect(Order order, string routingKey);
    public void PublishOrderTopic(Order order, string routingKey);
}
