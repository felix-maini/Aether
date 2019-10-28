using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using static Aether.Common.Utils;

namespace Aether.ServiceBus.Messages
{
    /// <summary>
    /// The base class that helps convert objects into binaries and back. It has four major methods
    /// - Serialize
    /// - Deserialize
    /// - GetMessageSize
    /// - ToString
    /// </summary>
    public abstract class AetherMessageElement
    {
        #region Serialize

        /// <summary>
        /// Serialize a this <see cref="AetherMessageElement"/>
        /// </summary>
        /// <returns>A byte representation of the <see cref="AetherMessageElement"/></returns>
        protected byte[] _serialize()
            => GetType()
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute)))
                .Where(member => member is PropertyInfo || member is FieldInfo)
                .Select(member => member.GetValue(this))
                .Select(ConvertToBytes)
                .Aggregate<byte[], byte[]>(null, (current, bytes) => IsNull(current)
                    ? bytes
                    : current.Concat(bytes).ToArray());

        /// <summary>
        /// The granular method that iterates over properties and serializes them into binaries.
        /// </summary>
        /// <param name="value">The value of that member.</param>
        /// <returns>The byte representation of that member.</returns>
        private static byte[] ConvertToBytes(object value)
        {
            // If the member null, return the binary value for null.
            if (IsNull(value)) return Constants.Null;

            // Get the type of the value
            var type = value.GetType();

            // If the member is NOT of type array...
            if (!type.IsArray)
                // ... convert the single value
                return ConvertSingleElement(value);

            // ... else cast the value to an array
            var array = (Array) value;

            // Determine the length of the array
            var arrayLength = BitConverter.GetBytes(array.Length).ToArray();

            // Iterate over the elements of the array, call the ConvertToBytes method and concat the results.
            return array
                .Cast<object>()
                .Select(ConvertToBytes)
                .Aggregate(arrayLength, (bytes, element) => bytes.Concat(element).ToArray());
        }

        /// <summary>
        /// This method delegates the conversion of a single value.
        /// </summary>
        /// <param name="value">The value of the member.</param>
        /// <returns>The byte representation of that value.</returns>
        /// <exception cref="ArgumentException"></exception>
        private static byte[] ConvertSingleElement(object value)
        {
            byte[] bytes;
            var type = value.GetType();

            if (type.IsPrimitive)
                bytes = ConvertPrimitive(value);

            else if (type.IsValueType)
                bytes = ConvertValueType(value);

            else if (type.IsClass)
                bytes = ConvertClass(value);

            else
                // This case should never happen. 
                throw new ArgumentException($"Unsupported primitive type: {value.GetType().FullName}");

            return bytes;
        }

        /// <summary>
        /// Convert a primitive value into a byte array.
        /// </summary>
        /// <param name="value">The value of the member.</param>
        /// <returns>The byte representation of the value.</returns>
        private static byte[] ConvertPrimitive(object value)
            => value switch
            {
                byte val => BitConverter.GetBytes(val),
                bool val => BitConverter.GetBytes(val),
                ushort val => BitConverter.GetBytes(val),
                short val => BitConverter.GetBytes(val),
                uint val => BitConverter.GetBytes(val),
                int val => BitConverter.GetBytes(val),
                float val => BitConverter.GetBytes(val),
                ulong val => BitConverter.GetBytes(val),
                long val => BitConverter.GetBytes(val),
                double val => BitConverter.GetBytes(val),
                _ => throw new ArgumentException($"Unsupported type: {value.GetType().FullName}")
            };


        /// <summary>
        /// Convert a ValueType into a byte array. At the moment, ony the <see cref="DateTime"/> valueType is
        /// implemented. Needs to be extended.
        /// </summary>
        /// <param name="value">The value of the member.</param>
        /// <returns>The byte representation of the value.</returns>
        private static byte[] ConvertValueType(object value)
            => value switch
            {
                DateTime dateTimeValue => BitConverter.GetBytes(dateTimeValue.Ticks),
                _ => throw new ArgumentException($"Unsupported type: {value.GetType().FullName}")
            };


        /// <summary>
        /// Delegates the conversion of single class.
        /// </summary>
        /// <param name="value">The value of the member.</param>
        /// <returns>The byte representation of the value.</returns>
        private static byte[] ConvertClass(object value)
        {
            // If is null, return the representation of null
            if (IsNull(value)) return Constants.Null;

            // At the moment only string, AetherMessageElement and X509Certificate2 is implemented.
            var bytes = value switch
            {
                string val => Constants.Utf8Encoding.GetBytes(val),
                BaseAetherMessage val => val._serialize(),
                X509Certificate2 val => val.RawData,
                Type val => Constants.Utf8Encoding.GetBytes(val.AssemblyQualifiedName),
                _ => throw new ArgumentException($"Unsupported type: {value.GetType().FullName}")
            };

            // Each class has its length prepended. That is needed for a successful deserialization.
            return BitConverter.GetBytes(bytes.Length).Concat(bytes).ToArray();
        }

        #endregion


        #region Deserialize

        /// <summary>
        /// Deserializes a <see cref="BaseAetherMessage"/> 
        /// </summary>
        /// <param name="buffer">Binary representation of a <see cref="BaseAetherMessage"/>.</param>
        /// <param name="message">A new instance of a class inheriting from <see cref="BaseAetherMessage"/> which
        /// is the target of the deserialization.</param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        protected T _deserialize<T>(byte[] buffer, T message) where T : BaseAetherMessage, new()
        {
            message.GetType()
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute)))
                .Where(member => member is PropertyInfo || member is FieldInfo)
                .Aggregate<MemberInfo, uint>(0,
                    (bufPos, property) => ConvertToValue(property, buffer, message, bufPos));

            return message;
        }


        /// <summary>
        /// Convert a part of the byte array into the value of type of the member. It does this by first inspecting
        /// the member for its type and then creating a converter <code>Func{byte[], object}</code>. Together with
        /// the calculated size of the value in bytes, the converter is then eventually passed to the
        /// <see cref="GetValue"/> function that executes the converter and actually transforms the bytes into the value.
        /// </summary>
        /// <param name="member">The target of the conversion.</param>
        /// <param name="buffer">The buffer containing the serialized message.</param>
        /// <param name="message">The target of the entire conversion.</param>
        /// <param name="bufPos">The offset inside the buffer tell where to slice the next bytes from.</param>
        /// <returns></returns>
        private uint ConvertToValue(MemberInfo member, byte[] buffer, AetherMessageElement message, uint bufPos)
        {
            var type = member.Type();

            object value;

            uint valueSize;

            Func<byte[], object> converter;

            // If type is NOT an array
            if (!type.IsArray)
            {
                // Get the converter and the value size for this single value
                (converter, valueSize) = ConvertSingleElement(type, buffer, bufPos);

                // All classes have their size prepended. We need to skip them to get the value from its actual position
                if (type.IsClass)
                    bufPos += Constants.SizeSize;

                // Slice the correct amount of bytes of the buffer and execute the converter function on it the get the
                // value meant for the member
                value = GetValue(converter, buffer, bufPos, valueSize);

                // Increase the offset by the size of the value
                bufPos += valueSize;
            }
            else
            {
                // Get the type of the elements in the array
                var elementType = type.GetElementType();

                // Get the size of the array
                var arraySize = GetArraySize(bufPos, buffer);

                // Skip the four bytes that held the length of the array
                bufPos += Constants.ArraySize;

                // Instantiate a new array with the proper size 
                var elementArray = Array.CreateInstance(elementType, arraySize);

                // Iterate over the array
                for (var i = 0; i < arraySize; i++)
                {
                    // Get the converter and size
                    (converter, valueSize) = ConvertSingleElement(elementType, buffer, bufPos);

                    // Skip the prepended size bytes 
                    if (elementType.IsClass)
                        bufPos += Constants.SizeSize;

                    // Get the value and immediately set it at its array position
                    elementArray.SetValue(GetValue(converter, buffer, bufPos, valueSize), i);

                    // Increase the buffer offset by the size of the value
                    bufPos += valueSize;
                }

                // Set the array as the value for the member
                value = elementArray;
            }

            // Give the member its value
            member.SetValue(message, value);

            // Return the buffer position. Each member needs to continue where the last ended
            return bufPos;
        }

        /// <summary>
        /// Delegate the conversion of single element to more specialized functions.
        /// </summary>
        /// <param name="type">Type of the member</param>
        /// <param name="buffer">The byte buffer</param>
        /// <param name="bufPos">The offset of the buffer</param>
        /// <returns></returns>
        private (Func<byte[], object>, uint) ConvertSingleElement(Type type, byte[] buffer, uint bufPos)
        {
            // Prepare a function that takes some object and returns a byte array. Depending on the type of the
            // member, the function will contain different conversion methods.
            Func<byte[], object> converter = null;

            // Initialize the value size with 0. It holds the size of the value of the member converted and is 
            // added the index of the buffer when the converted value is attached to it.
            uint valueSize = 0;

            // Primitive Type
            if (type.IsPrimitive)
                (converter, valueSize) = ConvertPrimitive(type);

            // Value Type
            else if (type.IsValueType)
                (converter, valueSize) = ConvertValueType(type);

            // Class Type
            else if (type.IsClass)
                (converter, valueSize) = ConvertClass(type)(buffer, bufPos);

            return (converter, valueSize);
        }

        /// <summary>
        /// Create the converter for when the type is a class type
        /// </summary>
        /// <param name="type">Type of the member</param>
        /// <returns>A function that can be invoked with the buffer and buffer position to create the converter
        /// function.</returns>
        /// <exception cref="ArgumentException"></exception>
        private Func<byte[], uint, (Func<byte[], object>, uint)> ConvertClass(Type type)
        {
            Func<byte[], uint, (Func<byte[], object>, uint)> converterBuilder;

            // BaseAetherMessage 
            if (type.IsSubclassOf(typeof(AetherMessageElement)))
                converterBuilder = BuildClassConverter(
                    bytes => BaseAetherMessage.Deserialize(bytes, type),
                    bytes => null);

            // X509Certificate2
            else if (type == typeof(X509Certificate2))
                converterBuilder = BuildClassConverter(
                    bytes => new X509Certificate2(bytes),
                    bytes => null);

            // String
            else if (type == typeof(string))
                converterBuilder = BuildClassConverter(
                    bytes => Constants.Utf8Encoding.GetString(bytes),
                    bytes => null);

            // Type
            else if (type == typeof(Type))
                converterBuilder = BuildClassConverter(
                    bytes => Type.GetType(Constants.Utf8Encoding.GetString(bytes)),
                    bytes => null);


            else
                throw new ArgumentException($"Unsupported type {type.FullName}");

            return converterBuilder;
        }

        /// <summary>
        /// Iterates over all primitive types and converts them respectively
        /// </summary>
        /// <param name="type">Type of the value</param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static (Func<byte[], object>, uint) ConvertPrimitive(Type type)
        {
            uint valueSize;

            Func<byte[], object> converter;

            // Byte: size 1
            if (type == typeof(byte))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(byte));
                converter = bytes => bytes[0];
            }
            // bool: size 1 (i think)
            else if (type == typeof(bool))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(bool));
                converter = bytes => BitConverter.ToBoolean(bytes);
            }
            // ushort: size 2
            else if (type == typeof(ushort))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(ushort));
                converter = bytes => BitConverter.ToUInt16(bytes);
            }
            // short: size 2
            else if (type == typeof(short))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(short));
                converter = bytes => BitConverter.ToInt16(bytes);
            }
            // uint: size 4
            else if (type == typeof(uint))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(uint));
                converter = bytes => BitConverter.ToUInt32(bytes);
            }
            // int: size 4
            else if (type == typeof(int))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(int));
                converter = bytes => BitConverter.ToInt32(bytes);
            }
            // float: size 4
            else if (type == typeof(float))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(float));
                converter = bytes => BitConverter.ToSingle(bytes);
            }
            // ulong: size 8
            else if (type == typeof(ulong))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(ulong));
                converter = bytes => BitConverter.ToUInt64(bytes);
            }
            // long: size 8
            else if (type == typeof(long))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(long));
                converter = bytes => BitConverter.ToInt64(bytes);
            }
            // double: size 8
            else if (type == typeof(double))
            {
                valueSize = (uint) Marshal.SizeOf(typeof(double));
                converter = bytes => BitConverter.ToDouble(bytes);
            }
            else
            {
                // This case should never happen. 
                throw new ArgumentException($"Unsupported primitive type: {type.FullName}");
            }

            return (converter, valueSize);
        }

        /// <summary>
        /// Create a converter for the value types.
        /// </summary>
        /// <param name="type"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        private static (Func<byte[], object>, uint) ConvertValueType(Type type)
        {
            uint valueSize;

            Func<byte[], object> converter;

            // DateTime
            if (type == typeof(DateTime))
            {
                valueSize = Constants.DateTimeSize;
                converter = bytes => DateTime.FromBinary(BitConverter.ToInt64(bytes));
            }
            else
                // Probably extensible
                throw new ArgumentException($"Unsupported ValueType {type.FullName}");

            return (converter, valueSize);
        }


        // If is type string or array of objects, the number of elements is encoded into binary as four byte uint right
        // before the actual data. 
        private static uint GetArraySize(uint position, IEnumerable<byte> sourceBuffer) =>
            BitConverter.ToUInt32(sourceBuffer.Skip((int) position).Take((int) Constants.ArraySize).ToArray());


        /// <summary>
        /// Cut the bytes from the byte array and convert them into a value.
        /// </summary>
        /// <param name="converter">The function that takes the bytes and returns a value</param>
        /// <param name="sourceBuffer"></param>
        /// <param name="position"></param>
        /// <param name="valueSize"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        private static object GetValue(Func<byte[], object> converter, IReadOnlyCollection<byte> sourceBuffer,
            uint position,
            uint valueSize)
        {
            if (IsNull(sourceBuffer) || sourceBuffer.Count < (position + valueSize) || IsNull(converter))
                throw new ArgumentNullException($"Encoder is null");

            var slice = sourceBuffer.Skip((int) position).Take((int) valueSize).ToArray();
            return converter(slice);
        }

        #endregion

        #region GetSize

        protected uint _getMessageSize()
            => GetType()
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member => member is PropertyInfo || member is FieldInfo)
                .Select(property => property.GetValue(this))
                .Select(SizeOf)
                .Aggregate<uint, uint>(0, (messageSize, valueSize) => messageSize + valueSize);


        private static uint SizeOf(object value)
            =>
                IsNull(value)
                    ? Constants.NullSize
                    : value.GetType().IsArray
                        ? ((Array) value).Cast<object>().Aggregate<object, uint>(Constants.ArraySize,
                            (current, element) =>
                            {
                                Console.WriteLine($"Current: {current}");
                                return current + SizeOfSingleElement(element);
                            })
                        : SizeOfSingleElement(value);

        private static uint SizeOfSingleElement(object value)
        {
            var type = value.GetType();
            uint size = 0;

            if (type.IsPrimitive)
                size = SizeOfPrimitives(value);

            else if (type.IsValueType)
                size = SizeOfValueType(value);

            else if (type.IsClass)
                size = SizeOfClass(value);

            return size;
        }

        private static uint SizeOfClass(object value)
            => IsNull(value)
                ? Constants.NullSize
                : Constants.SizeSize + value switch
                {
                    AetherMessageElement val => val._getMessageSize(),
                    string val => (uint) val.Length,
                    X509Certificate2 val => (uint) val.RawData.Length,
                    _ => throw new ArgumentException($"Unsupported type: {value.GetType().FullName}")
                };

        private static uint SizeOfValueType(object value)
            => IsNull(value)
                ? Constants.NullSize
                : value switch
                {
                    DateTime val => SizeOfPrimitives(val.Ticks),
                    _ => throw new ArgumentException($"Unsupported type: {value.GetType().FullName}")
                };


        private static uint SizeOfPrimitives(object value)
            => _traversePrimitives(valueToCast => (uint) Marshal.SizeOf(valueToCast))(value);

        #endregion

        #region ToString

        public override string ToString()
            => GetType()
                .GetMembers(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Where(member => !member.IsDefined(typeof(CompilerGeneratedAttribute)))
                .Where(member => member is PropertyInfo || member is FieldInfo)
                .Aggregate(new StringBuilder(), (current, member) =>
                    current.AppendLine($"{member.Name}: {member.GetValue(this)}\n"))
                .ToString();

        #endregion

        #region Helper Classes and Funktions

        private static Func<object, T> _traversePrimitives<T>(Func<object, T> func)
            =>
                value => value switch
                {
                    byte val => func(val),
                    bool val => func(val),
                    ushort val => func(val),
                    short val => func(val),
                    uint val => func(val),
                    int val => func(val),
                    float val => func(val),
                    ulong val => func(val),
                    long val => func(val),
                    double val => func(val),
                    _ => throw new ArgumentException($"Unsupported type: {value.GetType().FullName}")
                };

        private static Func<byte[], uint, (Func<byte[], object>, uint)> BuildClassConverter(
            Func<byte[], object> valueFunc, Func<byte[], object> nullFunc)
        {
            return (buffer, bufPos) =>
            {
                Func<byte[], object> converter;
                // The certificates are four bytes int that tell the length of the byte array of the certificate
                // certificate byte array size: [int<4 byte>, certificate bytes<array size> ]
                var valueSize = GetArraySize(bufPos, buffer);

                // If the value size is greater than 0 there lies an actual
                if (valueSize > 0)
                    // Prepare the converter for creating the certificate
                    converter = valueFunc;
                // If the value is 0, that that means that the certificate member is null.
                // Null is represented as a 0 integer
                else if (valueSize == 0)
                    // And tell the converter that it is supposed to simply return null
                    converter = nullFunc;
                else
                    // Either the message is faulty or someone manipulated those bytes
                    throw new ArgumentException(
                        $"The size of the byte array is neither greater 0 nor exactly 0. That cannot be.");
                return (converter, valueSize);
            };
        }

        /// <summary>
        ///  A few constant values that are required throughout the conversions.
        /// </summary>
        private static class Constants
        {
            public static UTF8Encoding Utf8Encoding =>
                (IsNull(_utf8Encoding) ? (_utf8Encoding = new UTF8Encoding()) : _utf8Encoding);

            private static UTF8Encoding _utf8Encoding;

            public static uint DateTimeSize =>
                (_dateTimeSize == 0 ? (_dateTimeSize = (uint) Marshal.SizeOf(DateTime.Now.Ticks)) : _dateTimeSize);

            private static uint _dateTimeSize;

            public static uint SizeSize => ArraySize;

            public static uint NullSize => ArraySize;

            public static uint ArraySize =>
                (_arraySize == 0 ? (_arraySize = (uint) Marshal.SizeOf<int>()) : _arraySize);

            private static uint _arraySize;

            public static byte[] Null => _null ??= new byte[] {0, 0, 0, 0};

            private static byte[] _null;
        }

        #endregion
    }
}