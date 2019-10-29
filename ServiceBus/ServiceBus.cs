using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Mqtt;
using System.Reflection;
using Newtonsoft.Json;
using static Aether.Common.Utils;

namespace Aether.ServiceBus
{
    /// <summary>
    /// This is one of the heart pieces of the Aether project. It contains the <see cref="IMqttClient"/> for the
    /// communication with the mqtt message broker, as well as the registry for the message processors. Message
    /// processors are the instances that contains with <see cref="T:PubSubAttribute"/> attributed methods.
    /// With each incoming message the registry is traversed to forward the message to all methods that have subscribed
    /// to the topic.
    /// </summary>
    public class ServiceBus
    {
        #region Private Fields

        private readonly ServiceBusConfiguration _configuration;

        private SessionState _sessionState;

        /// <summary>
        /// The interface to the mqtt message broker.
        /// </summary>
        private readonly IMqttClient _bus;

        /// <summary>
        /// The registry. The dictionary maps the name of a mqtt topic to a list of subscribed methods. The methods
        /// are encapsulated in action elements.
        /// </summary>
        private readonly Dictionary<string, Dictionary<string, Action<byte[]>>> _consumers =
            new Dictionary<string, Dictionary<string, Action<byte[]>>>();

        #endregion

        #region Contstructors

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="mqttClient">An already existing IMqttClient </param>
        /// <param name="configuration"></param>
        public ServiceBus(IMqttClient mqttClient, ServiceBusConfiguration configuration = null)
        {
            _configuration = configuration ?? new ServiceBusConfiguration();
            _bus = mqttClient;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="host">The hostname or IP or the mqtt message broker</param>
        /// <param name="port">The port on the host of the mqtt message broker</param>
        /// <param name="configuration"></param>
        public ServiceBus(string host, int port, ServiceBusConfiguration configuration = null)
        {
            _configuration = configuration ?? new ServiceBusConfiguration();

            var connectionString = host + ":" + port;
            _bus = MqttClient.CreateAsync(connectionString).Result;
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="connectionString">A full connection string to the mqtt message broker</param>
        /// <param name="configuration"></param>
        public ServiceBus(string connectionString, ServiceBusConfiguration configuration = null)
        {
            _configuration = configuration ?? new ServiceBusConfiguration();

            _bus = MqttClient.CreateAsync(connectionString).Result;
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Unsubscribe a dynamically added subscription.
        /// </summary>
        /// <param name="guid"></param>
        /// <returns>
        /// True if the subscription was actually registered and has been successfully removed.
        /// False if the action was not found in the registry.
        /// </returns>
        public void UnsubscribeFromTopic(Guid guid)
        {
            // Convert guid to string
            var id = guid.ToString();

            // Get topic
            var topic = _consumers.Keys.First(key => _consumers[key].ContainsKey(id));

            // If the topic was not found, return
            if (string.IsNullOrWhiteSpace(topic)) return;

            // Remove the action
            if (_consumers[topic].ContainsKey(id))
                _consumers[topic].Remove(id);

            // If it was the last registered action for the topic, remove the topic
            if (_consumers[topic].Any()) return;
            _bus.UnsubscribeAsync(topic);
            _consumers.Remove(topic);
        }

        /// <summary>
        /// Subscribe an action to a topic
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="action"></param>
        /// <param name="qoS"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public Guid SubscribeToTopic<T>(string topic, Action<T> action,
            MqttQualityOfService qoS = MqttQualityOfService.ExactlyOnce) where T : class, new()
        {
            // Create a new id for the action
            var guid = Guid.NewGuid();
            var id = guid.ToString();

            // If there was not prior subscription to that topic, subscribe for that topic at the mqtt message broker
            if (!_consumers.ContainsKey(topic))
                _bus.SubscribeAsync(topic, qoS);

            // Get the list of actions that belong to that topic
            if (!_consumers.ContainsKey(topic))
                _consumers[topic] = new Dictionary<string, Action<byte[]>>();

            // Add the new action
            // The action of the caller is wrapped in an action that does the casting of the message
            _consumers[topic][id] = bytes =>
            {
                T message = null;
                try
                {
                    var jsonString = Utf8Encoding.GetString(bytes);
                    message = JsonConvert.DeserializeObject<T>(jsonString);
                }
                catch (Exception e)
                {
                    // ignore
                }

                // Execute the callers action with the message as parameter
                action(message);
            };

            return guid;
        }

        /// <summary>
        /// Connect the Mqtt client to the message broker and subscribe the message stream
        /// </summary>
        /// <returns></returns>
        public bool TryConnect()
        {
            _sessionState = _bus.ConnectAsync().Result;
            _subscribe();
            return _bus.IsConnected;
        }

        /// <summary>
        /// Publish a message to the mqtt message broker.
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="message"></param>
        /// <param name="qoS"></param>
        /// <typeparam name="T"></typeparam>
        public void Publish<T>(string topic, T message, MqttQualityOfService qoS = MqttQualityOfService.ExactlyOnce)
            where T : class
        {
            var jsonString = JsonConvert.SerializeObject(message);
            var applicationMessage = new MqttApplicationMessage(topic, Utf8Encoding.GetBytes(jsonString));
            _bus.PublishAsync(applicationMessage, qoS);
        }

        /// <summary>
        /// Notifications do not have a payload and are only meant notify a consumer.
        /// </summary>
        /// <param name="topic"></param>
        /// <param name="qoS"></param>
        public void Notify(string topic, MqttQualityOfService qoS = MqttQualityOfService.ExactlyOnce)
        {
            var applicationMessage = new MqttApplicationMessage(topic, new byte[] { });
            _bus.PublishAsync(applicationMessage, qoS);
        }

        /// <summary>
        /// Top level method for registering a message processor. Arbitrarily many message processors can be registered.
        /// </summary>
        /// <param name="messageProcessor">
        /// A class instance that contains the methods, that are being
        /// called when new messages arrive from the mqtt message bus.
        /// </param>
        public void RegisterMessageProcessor<T>(T messageProcessor) where T : class
        {
            _register(messageProcessor);

            _subscribeToTopics(messageProcessor);

        }

        /// <summary>
        /// Here we subscribe to all the topics of interest. To do so, we first iterate over all methods of the
        /// class instance to find the methods with the <see cref="PubSubAttribute"/>. We then extract
        /// all the topics and register them at the mqtt message bus.
        /// </summary>
        /// <param name="messageProcessor">
        /// The class instance that contains with <see cref="T:PubSubAttribute"/> attributed methods.
        /// </param>
        private void _subscribeToTopics<T>(T messageProcessor) where T : class
        {
            messageProcessor.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.IsDefined(typeof(PubSubAttribute)))
                .Select(method => method.GetCustomAttribute<PubSubAttribute>())
                .ForEach(attribute => _bus.SubscribeAsync(attribute.Topic,
                    attribute.QoS >= _configuration.ConsumeMinQoS
                        ? attribute.QoS <= _configuration.ConsumeMaxQoS
                            ? attribute.QoS
                            : _configuration.ConsumeMaxQoS
                        : _configuration.ConsumeMinQoS
                ));
        }

        #endregion

        #region Private Methods

        /// <summary>
        /// Here we register the method that is being called by the <see cref="IMqttClient"/> whenever a message
        /// arrives from on of the topics we have subscribed ourselves to.
        /// </summary>
        private void _subscribe()
            =>
                // Each new message will be passed through the MessageStream
                _bus.MessageStream.Subscribe(msg =>
                {
                    // Return if there are no actions registered for this topic
                    if (!_consumers.ContainsKey(msg.Topic)) return;

                    var dict = _consumers[msg.Topic];
                    
                    foreach (var key in dict.Keys)
                        dict[key](msg.Payload);

//                        actions.Values.AsParallel().ForAll(action => action(msg.Payload));
                });

        /// <summary>
        /// Top level method that delegates the registration of the message processor to the pure
        /// consumers and the consumers and responders. The processes is slightly different for each, since the latter
        /// needs to respond via the mqtt message broker.
        /// </summary>
        /// <param name="messageProcessor">The class instance which methods are being registered.</param>
        private void _register<T>(T messageProcessor) where T : class
        {
            _registerNotifiers(messageProcessor);
            _registerConsumers(messageProcessor);
            _registerConsumersAndProviders(messageProcessor);
        }

        /// <summary>
        /// _register the methods for the consumers and responders.
        /// </summary>
        /// <param name="messageProcessor"></param>
        private void _registerNotifiers<T>(T messageProcessor) where T : class =>
            // First we need to select the methods that are attributed with the ConsumeAndProduce attribute.
            messageProcessor.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => !method.IsDefined(typeof(Consume)))
                .Where(method => !method.IsDefined(typeof(ConsumeAndRespond)))
                .Where(method => method.IsDefined(typeof(Notify)))
                .Where(method => method.GetParameters().Length == 0)
                .Where(method => method.ReturnType == typeof(void))
                .ForEach(method =>
                    {
                        // Get the attribute 
                        var attribute = method.GetCustomAttribute<Notify>();

                        // Create a unique identifier. It makes sure, that tow methods with the same name from different
                        // commandProcessors do not override each other.
                        var uniqueIdentifier = messageProcessor.GetType().FullName + _buildIdentifier(method);

                        // Get the existing list of consumers for that topic OR create a new one, if none existed.
                        if (!_consumers.ContainsKey(attribute.Topic))
                            _consumers[attribute.Topic] = new Dictionary<string, Action<byte[]>>();

                        // Add an IdentifiableAction to the list of consumers. It takes its identifier and the payload
                        // of the message that arrived.
                        _consumers[attribute.Topic][uniqueIdentifier] =
                            bytes =>
                            {
                                // Simply invoke the method without any payload or parameters.
                                try
                                {
                                    method.Invoke(messageProcessor, null);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    Console.WriteLine(
                                        $"Invoking method {method.Name} failed without any parameter");
                                    throw;
                                }

                                // Should the logger string be set...
                                if (!string.IsNullOrWhiteSpace(attribute.Logger))
                                    // ... send the notification to the logger
                                    _bus.PublishAsync(new MqttApplicationMessage(attribute.Logger, null),
                                        attribute.LoggerQoS);
                            };
                    }
                );


        /// <summary>
        /// _register the methods for the consumers and responders.
        /// </summary>
        /// <param name="messageProcessor"></param>
        private void _registerConsumersAndProviders<T>(T messageProcessor) where T : class =>
            // First we need to select the methods that are attributed with the ConsumeAndProduce attribute.
            messageProcessor.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => !method.IsDefined(typeof(Consume)))
                .Where(method => method.IsDefined(typeof(ConsumeAndRespond)))
                .Where(method => method.GetParameters().Length == 1)
                .Where(method => method.ReturnType.IsClass)
                .Where(method => method.GetParameters()[0].ParameterType.IsClass)
                .ForEach(method =>
                    {
                        // Get the attribute 
                        var attribute = method.GetCustomAttribute<ConsumeAndRespond>();

                        // Create a unique identifier. It makes sure, that tow methods with the same name from different
                        // commandProcessors do not override each other.
                        var uniqueIdentifier = messageProcessor.GetType().FullName + _buildIdentifier(method);

                        // Get the existing list of consumers for that topic OR create a new one, if none existed.
                        if (!_consumers.ContainsKey(attribute.Topic))
                            _consumers[attribute.Topic] = new Dictionary<string, Action<byte[]>>();

                        // Add an IdentifiableAction to the list of consumers. It takes its identifier and the payload
                        // of the message that arrived.
                        _consumers[attribute.Topic][uniqueIdentifier] =
                            bytes =>
                            {
                                // Get the type of the parameter of the method about to be invoked
                                var type = method.GetParameters()[0].ParameterType;

                                // Deserialize 
                                object message;
                                try
                                {

                                    var jsonString = Utf8Encoding.GetString(bytes);
                                    message = JsonConvert.DeserializeObject(jsonString, type);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    Console.WriteLine(
                                        $"{nameof(JsonConvert.DeserializeObject)} failed for type {type.FullName}");
                                    throw;
                                }

                                // Invoke the method of the commandProcessor 
                                // We take the result, since we mean to return a message.
                                object returnValue;
                                try
                                {
                                    returnValue =
                                        method.Invoke(messageProcessor, new[] {message});

                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    throw;
                                }

                                // Should the logger string be set...
                                if (!string.IsNullOrWhiteSpace(attribute.Logger))
                                    // ..send the received message to the message logger and ...
                                    _bus.PublishAsync(new MqttApplicationMessage(attribute.Logger, bytes),
                                        attribute.LoggerQoS);

                                // Should the logger string be set...
                                if (NonNull(returnValue))
                                {
                                    var jsonString = JsonConvert.ToString(returnValue);
                                    var returnBytes = Utf8Encoding.GetBytes(jsonString);
                                    // Send the result of the invocation to the respondTo topic 
                                    _bus.PublishAsync(
                                        new MqttApplicationMessage(attribute.RespondTo, returnBytes),
                                        attribute.QoS >= _configuration.RespondToMinQos
                                            ? attribute.QoS <= _configuration.RespondToMaxQos
                                                ? attribute.QoS
                                                : _configuration.ConsumeMaxQoS
                                            : _configuration.ConsumeMinQoS
                                    );
                                }
                            };
                    }
                );

        /// <summary>
        ///  _register the pure consumers
        /// </summary>
        /// <param name="messageProcessor">An class object which contains attributed methods to register</param>
        private void _registerConsumers<T>(T messageProcessor) where T : class =>
            // First we need to select the methods that are attributed with the Consume attribute.
            messageProcessor.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.IsDefined(typeof(Consume)))
                .Where(method => !method.IsDefined(typeof(ConsumeAndRespond)))
                .Where(method => method.ReturnType == typeof(void))
                .Where(method => method.GetParameters().Length == 1)
                .Where(method => method.GetParameters()[0].ParameterType.IsClass)
                .ForEach(method =>
                    {
                        // Get the attribute
                        var attribute = method.GetCustomAttribute<Consume>();

                        // Create a unique identifier. It makes sure, that tow methods with the same name from different
                        // commandProcessors do not override each other.
                        var uniqueIdentifier = messageProcessor.GetType().FullName + _buildIdentifier(method);

                        // Get the existing list of consumers for that topic OR create a new one, if none existed.
                        if (!_consumers.ContainsKey(attribute.Topic))
                            _consumers[attribute.Topic] = new Dictionary<string, Action<byte[]>>();

                        // Add an IdentifiableAction to the list of consumers. It takes its identifier and the payload
                        // of the message that arrived.
                        _consumers[attribute.Topic][uniqueIdentifier] =
                            bytes =>
                            {
                                // Get the type of the parameter of the method about to be invoked
                                var type = method.GetParameters()[0].ParameterType;

                                // Deserialize 
                                object message;
                                try
                                {
                                    var jsonString = Utf8Encoding.GetString(bytes);
                                    message = JsonConvert.DeserializeObject(jsonString, type);
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    Console.WriteLine(
                                        $"{nameof(JsonConvert.DeserializeObject)} failed for type {type.FullName}");
                                    throw;
                                }

                                try
                                {
                                    method.Invoke(messageProcessor, new[] {message});
                                }
                                catch (Exception e)
                                {
                                    Console.WriteLine(e);
                                    Console.WriteLine(
                                        $"Invoking method {method.Name} failed with parameter {type.FullName}");
                                    throw;
                                }

                                // Should the logger string be set...
                                if (!string.IsNullOrWhiteSpace(attribute.Logger))
                                    // ..send the received message to the message logger
                                    _bus.PublishAsync(
                                            new MqttApplicationMessage(attribute.Logger, bytes), attribute.LoggerQoS);
                            };
                    }
                );


        /// <summary>
        /// Build a unique string for each method of each message processor
        /// </summary>
        /// <param name="method"><see cref="MethodInfo"/> that describes the registered method.</param>
        /// <returns>The unique identifier string.</returns>
        private static string _buildIdentifier(MethodInfo method)
            => method.GetParameters()
                .Aggregate(method.ReturnType.FullName + method.Name,
                    (names, parameter) => names + "|" + parameter.Position + ':' + parameter.Name);

        #endregion
    }
}