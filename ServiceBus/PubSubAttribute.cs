using System;
using System.Net.Mqtt;

namespace Aether.ServiceBus
{
    /// <summary>
    /// Base attribute for the consumer and producer attributes. It contains a topic to which a method is subscribed.
    /// Further it has the Logger and LoggerQoS fields that indicate if a incoming message and/or outgoing message is
    /// to be sent to the topic of the Logger. It is meant to easy the functionality of general message logger.
    /// </summary>
    public abstract class PubSubAttribute : Attribute
    {
        public string Topic { get; }

        public MqttQualityOfService QoS { get; }
        public MqttQualityOfService LoggerQoS { get; }

        public string Logger { get; }

        protected PubSubAttribute(string topic, MqttQualityOfService qoS, string logger, MqttQualityOfService loggerQoS)
        {
            Topic = topic;
            QoS = qoS;
            LoggerQoS = loggerQoS;
            Logger = logger;
        }
    }

    /// <summary>
    /// The ConsumeAndRespond attribute subscribes itself to a topic and whenever it is invoked, the return value of the
    /// method is sent to the respondTo topic. It is message parser of some sort.
    /// </summary>
    public class ConsumeAndRespond : PubSubAttribute
    {
        public string RespondTo { get; }

        public ConsumeAndRespond(string topic, string respondTo,
            MqttQualityOfService qoS = MqttQualityOfService.AtMostOnce, string logger = null,
            MqttQualityOfService loggerQoS = MqttQualityOfService.AtMostOnce) : base(topic, qoS, logger, loggerQoS)
        {
            RespondTo = respondTo;
        }
    }

    /// <summary>
    /// The <see cref="Consume"/>  attribute is the simplest. It subscribes to a topic and processes the message without
    /// any other action. A method that is attributed with the <see cref="Consume"/> attribute must have a void return
    /// value. Should that not be the case. A exception will be thrown when registering the
    /// <see cref="ICommandProcessor"/>.
    /// </summary>
    public class Consume : PubSubAttribute
    {
        public Consume(string topic, MqttQualityOfService qoS = MqttQualityOfService.AtMostOnce, string logger = null,
            MqttQualityOfService loggerQoS = MqttQualityOfService.AtMostOnce) : base(topic, qoS, logger, loggerQoS)
        {
        }
    }
    
    /// <summary>
    /// This attribute marks a method as notifiable. It is not allowed to have any parameters and will not receive
    /// any payload.
    /// </summary>
    public class Notify : PubSubAttribute
    {
        public Notify(string topic, MqttQualityOfService qoS, string logger, MqttQualityOfService loggerQoS) : base(topic, qoS, logger, loggerQoS)
        {
        }
    }
}