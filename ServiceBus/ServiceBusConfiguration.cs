using System.Net.Mqtt;

namespace Aether.ServiceBus
{
    public class ServiceBusConfiguration
    {
        public bool StrictCasting { get; set; } = true;

        public MqttQualityOfService ConsumeDefaultQoS { get; set; } = MqttQualityOfService.ExactlyOnce;

        public MqttQualityOfService ConsumeAndRespondDefaultQoS { get; set; } = MqttQualityOfService.ExactlyOnce;
    }
}