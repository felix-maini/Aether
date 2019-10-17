using System;

namespace Aether.ServiceBus.Messages
{
    /// <summary>
    /// The class that allows deserialization and serialization of objects. It serializes objects into a binary format
    /// and is able to reconstruct them from that format into its former objects. The reason why a new serialization
    /// was needed for the Aether project is, that with existing tools it is impossible to deserialize a binary into
    /// a type, that is determined at runtime instead of compiletime. All this methods does is delegate to the
    /// <see cref="AetherMessageElement"/> class, that contains the entire logic of converting the objects.
    /// </summary>
    public class BaseAetherMessage : AetherMessageElement
    {
        /// <summary>
        /// Converts a object of BaseAetherMessage into a <code>byte[]</code>.
        /// </summary>
        /// <returns>The binary representation of an object.</returns>
        public byte[] ToBytes() => _serialize();

        /// <summary>
        /// Calculate the size of the message in binary format, without converting it first.
        /// </summary>
        /// <returns>An integer that holds the size of the message in bytes.</returns>
        public int MessageSize() => (int) _getMessageSize();

        /// <summary>
        /// Base Constructor.
        /// </summary>
        public BaseAetherMessage()
        {
        }

        /// <summary>
        /// Take a <code>byte[]</code> and fill its own properties with it.
        /// </summary>
        /// <param name="bytes">The binary representation of a BaseAetherMessage</param>
        public void Deserialize(byte[] bytes)
        {
            _deserialize(bytes, this);
        }

        /// <summary>
        /// Instantiates a <see cref="BaseAetherMessage"/> from binary.
        /// </summary>
        /// <param name="bytes">The binary representation of a <see cref="BaseAetherMessage"/></param>
        /// <param name="type">A type that must inherit from the <see cref="BaseAetherMessage"/>.</param>
        /// <returns>A <see cref="BaseAetherMessage"/></returns>
        /// <exception cref="ArgumentException"></exception>
        public static BaseAetherMessage Deserialize(byte[] bytes, Type type)
        {
            if (!typeof(BaseAetherMessage).IsAssignableFrom(type))
                throw new ArgumentException($"The type {type.FullName} does not inherit from {typeof(BaseAetherMessage).FullName}");
            
            var instance = (BaseAetherMessage) Activator.CreateInstance(type);
            instance._deserialize(bytes, instance);
            return instance;
        }

        /// <summary>
        /// Constructor from which a <see cref="BaseAetherMessage"/> can be built.
        /// </summary>
        /// <param name="bytes">The binary representation of a <see cref="BaseAetherMessage"/>.</param>
        public BaseAetherMessage(byte[] bytes)
        {
            _deserialize(bytes, this);
        }
    }
}