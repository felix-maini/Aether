using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

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
        /// Extension method for the MemberInfo to make allow for combining PropertyInfo and FieldInfo elements.
        /// </summary>
        /// <param name="member"></param>
        /// <param name="instance"></param>
        /// <param name="values"></param>
        public static void SetValue(this MemberInfo member, object instance, object values)
        {
            switch (member)
            {
                case PropertyInfo info:
                    info.SetValue(instance, values);
                    break;
                case FieldInfo info:
                    info.SetValue(instance, values);
                    break;
            }
        }

        /// <summary>
        /// Extension method for the MemberInfo to make allow for combining PropertyInfo and FieldInfo elements.
        /// </summary>
        /// <param name="member"></param>
        public static Type Type(this MemberInfo member)
            => member switch
            {
                PropertyInfo info => info.PropertyType,
                FieldInfo info => info.FieldType,
            };

        /// <summary>
        /// Extension method for the MemberInfo to make allow for combining PropertyInfo and FieldInfo elements.
        /// </summary>
        /// <param name="member"></param>
        /// <param name="instance"></param>
        /// <returns></returns>
        public static object GetValue(this MemberInfo member, object instance)
            => member switch
            {
                PropertyInfo info => info.GetValue(instance),
                FieldInfo info => info.GetValue(instance),
                _ => null
            };
    }
}