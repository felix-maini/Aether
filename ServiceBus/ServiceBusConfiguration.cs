using System.Net.Mqtt;

namespace Aether.ServiceBus
{
    public class ServiceBusConfiguration
    {
        /// <summary>
        /// Determines whether it is allowed to receive messages that have been converted into a fitting message type
        /// other than the original MessageType. With strict conversion TRUE, only the sent message type has to be the
        /// received message type or else the packet gets dropped.
        /// </summary>
        /// <remarks>
        /// This can happen, when two messages consist out of the same properties in the same order. It then is possible
        /// to send one message type but have a different receiving message type with the same content.
        /// </remarks>
        public bool StrictConversion { get; set; } = true;

        public MqttQualityOfService ConsumeMinQoS { get; set; } = MqttQualityOfService.AtMostOnce;

        public MqttQualityOfService ConsumeMaxQoS { get; set; } = MqttQualityOfService.ExactlyOnce;

        public MqttQualityOfService RespondMinQos { get; set; } = MqttQualityOfService.AtMostOnce;
        
        public MqttQualityOfService RespondMaxQos { get; set; } = MqttQualityOfService.ExactlyOnce;
    }
}