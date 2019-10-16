using System;
using System.Collections.Generic;
using System.Linq;

namespace Aether.Common
{
    /// <summary>
    /// Static class with helper functions.
    /// </summary>
    public static class Utils
    {
        /// <summary>
        /// A easy check if a value is null or not.
        /// </summary>
        /// <param name="obj">The object you want to check.</param> 
        /// <returns>A bool that indicates whether the object was null or not.</returns> 
        public static bool IsNull(object obj) => obj == null;

        /// <summary>
        /// A easy check if a value is NOT null. Reverse function of <see cref="IsNull"/>
        /// </summary>
        /// <param name="obj">The object you want to check.</param>
        /// <returns>A bool that indicates whether the object was null or not.</returns>
        public static bool NonNull(object obj) => !IsNull(obj);

        /// <summary>
        /// An extension method to iterate over a collection without a return value.
        /// </summary>
        /// <param name="source">The <see cref="IEnumerable{T}"/> collection you want to iterate over.</param> 
        /// <param name="action">The <see cref="Action{T}"/> that is executed with each element of the collection.</param>
        /// <typeparam name="T">Type of the elements in the collection. Has no constraints.</typeparam>
        /// <exception cref="ArgumentNullException">In case the <see cref="IEnumerable{T}"/> or the <see cref="Action{T}"/> is null.</exception>
        public static void ForEach<T>(this IEnumerable<T> source, Action<T> action)
        {
            if (IsNull(source))
                throw new ArgumentNullException(nameof(source));
            if (IsNull(action))
                throw new ArgumentNullException(nameof(action));
            foreach (var element in source)
                action(element);
        }

        /// <summary>
        /// An extension method to iterate over a collection of <see cref="Tuple{T,TU}"/>.
        /// </summary>
        /// <param name="source">The <see cref="IEnumerable{(T,TU)}"/> collection you want to iterate over.</param>
        /// <param name="action">The <see cref="Action{(T,TU)}"/> that is executed with each element of the collection.</param>
        /// <typeparam name="T">The type of the first element of the tuple.</typeparam>
        /// <typeparam name="TU">The type of the second element of tuple.</typeparam>
        /// <exception cref="ArgumentNullException"></exception>
        public static void ForEach<T, TU>(this IEnumerable<(T, TU)> source, Action<T, TU> action)
        {
            if (IsNull(source))
                throw new ArgumentNullException(nameof(source));
            if (IsNull(action))
                throw new ArgumentNullException(nameof(action));
            foreach (var (element0, element1) in source)
                action(element0, element1);
        }

        /// <summary>
        /// A optional type. Can either be null, or return a value of a generic type.
        /// </summary>
        /// <typeparam name="T">The type of the element, that can possible be contained by the optional.</typeparam>
        public class Optional<T> where T : class
        {
            private readonly T _obj = null;

            public Optional(T obj) => _obj = obj;

            public bool IsNull => _obj == null;

            public bool NonNull => !IsNull;

            public T Get()
            {
                if (IsNull)
                    throw new NullReferenceException(
                        "The object inside this Optional is null. Check with IsNull for null safety.");
                return _obj;
            }
        }
    }
}