using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Mqtt;
using System.Reflection;
using Aether.Common;
using Aether.ServiceBus.Messages;
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
        /// are encapsulated in <see cref="IdentifiableAction"/> elements.
        /// </summary>
        private readonly ConcurrentDictionary<string, ConcurrentBag<IdentifiableAction>> _consumers =
            new ConcurrentDictionary<string, ConcurrentBag<IdentifiableAction>>();

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
        /// Connect the Mqtt client to the message broker
        /// </summary>
        /// <returns></returns>
        public bool TryConnect()
        {
            _sessionState = _bus.ConnectAsync().Result;
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
            where T : BaseAetherMessage
        {
            var applicationMessage = new MqttApplicationMessage(topic, message.Serialize());
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
            Register(messageProcessor);

            SubscribeToTopics(messageProcessor);

            Subscribe();
        }

        /// <summary>
        /// Here we subscribe to all the topics of interest. To do so, we first iterate over all methods of the
        /// class instance to find the methods with the <see cref="PubSubAttribute"/>. We then extract
        /// all the topics and register them at the mqtt message bus.
        /// </summary>
        /// <param name="messageProcessor">
        /// The class instance that contains with <see cref="T:PubSubAttribute"/> attributed methods.
        /// </param>
        private void SubscribeToTopics<T>(T messageProcessor) where T : class
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

        /// <summary>
        /// Here we register the method that is being called by the <see cref="IMqttClient"/> whenever a message
        /// arrives from on of the topics we have subscribed ourselves to.
        /// </summary>
        private void Subscribe()
            =>
                // Each new message will be passed through the MessageStream
                _bus.MessageStream.Subscribe(msg =>
                {
                    // If consumers are registered for this topic... 
                    if (_consumers.ContainsKey(msg.Topic))
                        // ... then execute each of them them in parallel.
                        _consumers[msg.Topic].AsParallel().ForEach(action => action.Execute(msg.Payload));
                });

        /// <summary>
        /// Top level method that delegates the registration of the message processor to the pure
        /// consumers and the consumers and responders. The processes is slightly different for each, since the latter
        /// needs to respond via the mqtt message broker.
        /// </summary>
        /// <param name="messageProcessor">The class instance which methods are being registered.</param>
        private void Register<T>(T messageProcessor) where T : class
        {
            RegisterConsumers(messageProcessor);
            RegisterConsumersAndProviders(messageProcessor);
        }

        /// <summary>
        /// Register the methods for the consumers and responders.
        /// </summary>
        /// <param name="messageProcessor"></param>
        private void RegisterConsumersAndProviders<T>(T messageProcessor) where T : class =>
            // First we need to select the methods that are attributed with the ConsumeAndProduce attribute.
            messageProcessor.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => !method.IsDefined(typeof(Consume)))
                .Where(method => method.IsDefined(typeof(ConsumeAndRespond)))
                .Where(method => method.GetParameters().Length >= 1)
                .Where(method => typeof(BaseAetherMessage).IsAssignableFrom(method.GetParameters()[0].ParameterType))
                .Where(method => typeof(BaseAetherMessage).IsAssignableFrom(method.ReturnType))
                .ForEach(method =>
                    {
                        // Get the attribute 
                        var attribute = method.GetCustomAttribute<ConsumeAndRespond>();

                        // Create a unique identifier. It makes sure, that tow methods with the same name from different
                        // commandProcessors do not override each other.
                        var uniqueIdentifier = messageProcessor.GetType().FullName + _buildIdentifier(method);

                        // Get the existing list of consumers for that topic OR create a new one, if none existed.
                        var consumers = _consumers.GetOrAdd(attribute.Topic, new ConcurrentBag<IdentifiableAction>());

                        // Add an IdentifiableAction to the list of consumers. It takes its identifier and the payload
                        // of the message that arrived.
                        consumers.Add(
                            new IdentifiableAction(
                                uniqueIdentifier,
                                bytes =>
                                {
                                    BaseAetherMessage returnValue = null;
                                    // In case the method does not have a parameter, simply invoke the method without.
                                    if (method.GetParameters().Length == 0)
                                    {
                                        try
                                        {
                                            returnValue = method.Invoke(messageProcessor, null) as BaseAetherMessage;
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine(e);
                                            Console.WriteLine(
                                                $"Invoking method {method.Name} failed without any parameter");
                                            throw;
                                        }
                                    }
                                    else
                                    {
                                        // Get the type of the parameter of the method about to be invoked
                                        var type = method.GetParameters()[0].ParameterType;

                                        // Deserialize the bytes into a BaseAetherMessage
                                        BaseAetherMessage aetherMessage;
                                        try
                                        {
                                            aetherMessage = BaseAetherMessage.Deserialize(bytes, type);
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine(e);
                                            Console.WriteLine(
                                                $"{nameof(BaseAetherMessage.Deserialize)} failed for type {type.FullName}");
                                            throw;
                                        }

                                        // If strict converting is activated only continue if the aether message is valid
                                        if (_configuration.StrictConversion && !aetherMessage.IsValid())
                                            return;

                                        // Invoke the method of the commandProcessor with providing the BaseAetherMessage.
                                        // We take the result, since we mean to return a message.
                                        try
                                        {
                                            returnValue =
                                                method.Invoke(messageProcessor, new object[] {aetherMessage}) as
                                                    BaseAetherMessage;
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine(e);
                                            throw;
                                        }
                                    }

                                    // Should the logger string be set...
                                    if (!string.IsNullOrWhiteSpace(attribute.Logger))
                                        // ..send the received message to the message logger and ...
                                        _bus.PublishAsync(new MqttApplicationMessage(attribute.Logger, bytes),
                                            attribute.LoggerQoS);

                                    // Should the logger string be set...
                                    if (NonNull(returnValue))
                                        // Send the result of the invocation to the respondTo topic 
                                        _bus.PublishAsync(
                                            new MqttApplicationMessage(attribute.RespondTo, returnValue.Serialize()),
                                            attribute.QoS >= _configuration.RespondToMinQos
                                                ? attribute.QoS <= _configuration.RespondToMaxQos
                                                    ? attribute.QoS
                                                    : _configuration.ConsumeMaxQoS
                                                : _configuration.ConsumeMinQoS
                                        );
                                })
                        );
                    }
                );

        /// <summary>
        ///  Register the pure consumers
        /// </summary>
        /// <param name="messageProcessor">An class object which contains attributed methods to register</param>
        private void RegisterConsumers<T>(T messageProcessor) where T : class =>
            // First we need to select the methods that are attributed with the Consume attribute.
            messageProcessor.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(method => method.IsDefined(typeof(Consume)))
                .Where(method => !method.IsDefined(typeof(ConsumeAndRespond)))
                .Where(method => method.ReturnType == typeof(void))
                .Where(method => method.GetParameters().Length >= 1)
                .Where(method => typeof(BaseAetherMessage).IsAssignableFrom(method.GetParameters()[0].ParameterType))
                .ForEach(method =>
                    {
                        // Get the attribute
                        var attribute = method.GetCustomAttribute<Consume>();

                        // Create a unique identifier. It makes sure, that tow methods with the same name from different
                        // commandProcessors do not override each other.
                        var uniqueIdentifier = messageProcessor.GetType().FullName + _buildIdentifier(method);

                        // Get the existing list of consumers for that topic OR create a new one, if none existed.
                        var consumers = _consumers.GetOrAdd(attribute.Topic, new ConcurrentBag<IdentifiableAction>());

                        // Add an IdentifiableAction to the list of consumers. It takes its identifier and the payload
                        // of the message that arrived.
                        consumers.Add(
                            new IdentifiableAction(
                                uniqueIdentifier,
                                bytes =>
                                {
                                    // In case the method does not have a parameter, simply invoke the method without.
                                    if (method.GetParameters().Length == 0)
                                    {
                                        try
                                        {
                                            method.Invoke(messageProcessor, null);
                                            return;
                                        }
                                        catch (Exception e)
                                        {
                                            Console.WriteLine(e);
                                            Console.WriteLine(
                                                $"Invoking method {method.Name} failed without any parameters");
                                            throw;
                                        }
                                    }

                                    // Get the type of the parameter of the method about to be invoked
                                    var aetherMessageType = method.GetParameters()[0].ParameterType;

                                    // Deserialize the bytes into a BaseAetherMessage
                                    BaseAetherMessage aetherMessage;
                                    try
                                    {
                                        aetherMessage = BaseAetherMessage.Deserialize(bytes, aetherMessageType);
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine(e);
                                        Console.WriteLine(
                                            $"{nameof(BaseAetherMessage.Deserialize)} failed for type {aetherMessageType.FullName}");
                                        throw;
                                    }

                                    if (_configuration.StrictConversion && !aetherMessage.IsValid()) return;

                                    // Invoke the method of the commandProcessor with providing the BaseAetherMessage.
                                    try
                                    {
                                        method.Invoke(messageProcessor, new object[] {aetherMessage});
                                    }
                                    catch (Exception e)
                                    {
                                        Console.WriteLine(e);
                                        Console.WriteLine(
                                            $"Invoking method {method.Name} failed with parameter {aetherMessageType.FullName}");
                                        throw;
                                    }

                                    // Should the logger string be set...
                                    if (!string.IsNullOrWhiteSpace(attribute.Logger))
                                        // ..send the received message to the message logger
                                        _bus.PublishAsync(
                                            new MqttApplicationMessage(attribute.Logger, bytes), attribute.LoggerQoS);
                                })
                        );
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
    }

    /// <summary>
    /// This class simply encapsulates an <see cref="Action{T}"/> element to add the identifier attribute. It is
    /// supposed to be used to find and deregister consumers in runtime.
    /// </summary>
    public class IdentifiableAction
    {
        public string Identifier { get; }
        private Action<byte[]> Action { get; }

        public void Execute(byte[] bytes) => Action(bytes);

        public IdentifiableAction(string identifier, Action<byte[]> action)
        {
            Identifier = identifier;
            Action = action;
        }
    }
}