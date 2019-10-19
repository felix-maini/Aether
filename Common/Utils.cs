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
    }
}