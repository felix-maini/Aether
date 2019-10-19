using System.Net.Mqtt;

namespace Aether.ServiceBus
{
    public class ServiceBusConfiguration
    {
        public bool StrictCasting { get; set; } = true;

        public MqttQualityOfService ConsumeMinQoS { get; set; } = MqttQualityOfService.AtMostOnce;

        public MqttQualityOfService ConsumeMaxQoS { get; set; } = MqttQualityOfService.ExactlyOnce;

        public MqttQualityOfService RespondMinQos { get; set; } = MqttQualityOfService.AtMostOnce;
        
        public MqttQualityOfService RespondMaxQos { get; set; } = MqttQualityOfService.ExactlyOnce;
    }
}