# Aether 

Aether is a C# wrapper around the System.Net.Mqtt NuGet packet.

Aether was developed to reduce the overhead of coupling the functionality of a service with the servicebus.
Especially microservice architectures produce a lot of duplicate code for passing messages
from the message broker to the function that processes it. Aether was inspired by projects lik flask, where 
a simple decorator couples the method to a certain url. Aether uses C# attributes to couple methods to MQTT 
topics.

## Example
To couple a method to a topic, simply attach the attribute Consume to the method with the topic string.
In the example 1, whenever a message arrives for the topic '/events', the method is invoked and the parameter
instantiate with the received payload. 

### 1: Couple Method to Topic
```c#
public class MessageProcessor : IMessageProcessor
    {
        [Consume("/events"]
        public void EventProcessor(MyEvent event)
        {
            Console.WriteLine($"The event has ID: {event.Id}");
        }
    }
 ```
 
### Register the IMessageProcessor
Assuming a Mqtt message broker runs on the local machine.
```c#
public static class Program
{
    public static void Main(string[] args) 
    {
        var messageProcessor = new MessageProcessor();
        var serviceBus = new ServiceBus("localhost");
        
        serviceBus.RegisterMessageProcessor(messageProcessor);
    }
}
```

#  ToDo
The project is far from finished. This is a first working prototype. Surly it contains bugs and lacks functionality.
Its a personal project and I will continue to work on it. However, feel free to open issues or send me messages should
you have questions or ideas.
