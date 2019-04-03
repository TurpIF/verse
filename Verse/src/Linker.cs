using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse.Tools;

namespace Verse
{
    /// <summary>
    /// Utility class able to scan any type (using reflection) to automatically
    /// declare its fields to a decoder or encoder descriptor.
    /// </summary>
    public class Linker
    {
        /// <summary>
        /// Binding error occurred.
        /// </summary>
        public event Action<Type, string> Error;

        /// <summary>
        /// Scan entity type and declare fields using given decoder descriptor.
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="descriptor">Decoder descriptor</param>
        /// <returns>True if binding succeeded, false otherwise</returns>
        public bool LinkDecoder<TEntity>(IDecoderDescriptor<TEntity> descriptor)
        {
            return this.LinkDecoder(descriptor, new Dictionary<Type, object>());
        }

        /// <summary>
        /// Scan entity type and declare fields using given encoder descriptor.
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="descriptor">Encoder descriptor</param>
        /// <returns>True if binding succeeded, false otherwise</returns>
        public bool LinkEncoder<TEntity>(IEncoderDescriptor<TEntity> descriptor)
        {
            return this.LinkEncoder(descriptor, new Dictionary<Type, object>());
        }

        /// <summary>
        /// Shorthand method to create a decoder from given schema. This is
        /// equivalent to creating a new linker, use it on schema's decoder
        /// descriptor and throw exception whenever error event is fired.
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="schema">Entity schema</param>
        /// <returns>Entity decoder</returns>
        public static IDecoder<TEntity> CreateDecoder<TEntity>(ISchema<TEntity> schema)
        {
            var linker = new Linker();
            var message = "unknown";
            var type = typeof(TEntity);

            linker.Error += (t, m) =>
            {
                message = string.Empty;
                type = t;
            };

            if (!linker.LinkDecoder(schema.DecoderDescriptor))
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture,
                        "can't link decoder for type '{0}' (error with type '{1}', {2})", typeof(TEntity), type,
                        message), nameof(schema));

            return schema.CreateDecoder();
        }

        /// <summary>
        /// Shorthand method to create an encoder from given schema. This is
        /// equivalent to creating a new linker, use it on schema's encoder
        /// descriptor and throw exception whenever error event is fired.
        /// </summary>
        /// <typeparam name="TEntity">Entity type</typeparam>
        /// <param name="schema">Entity schema</param>
        /// <returns>Entity encoder</returns>
        public static IEncoder<TEntity> CreateEncoder<TEntity>(ISchema<TEntity> schema)
        {
            var linker = new Linker();
            var message = "unknown";
            var type = typeof(TEntity);

            linker.Error += (t, m) =>
            {
                message = string.Empty;
                type = t;
            };

            if (!linker.LinkEncoder(schema.EncoderDescriptor))
                throw new ArgumentException(
                    string.Format(CultureInfo.InvariantCulture,
                        "can't link encoder for type '{0}' (error with type '{1}', {2})", typeof(TEntity), type,
                        message), nameof(schema));

            return schema.CreateEncoder();
        }

        private bool LinkDecoder<T>(IDecoderDescriptor<T> descriptor, Dictionary<Type, object> parents)
        {
            object assign;
            Type inner;

            try
            {
                descriptor.IsValue();

                return true;
            }
            catch (InvalidCastException) // FIXME: hack
            {
            }

            var type = typeof(T);

            parents = new Dictionary<Type, object>(parents) {[type] = descriptor};

            // Target type is an array
            if (type.IsArray)
            {
                inner = type.GetElementType();
                assign = Linker.MakeAssignArray(inner);

                return this.LinkDecoderArray(descriptor, inner, assign, parents);
            }

            // Target type implements IEnumerable<> interface
            var filter = new TypeFilter((t, c) =>
                t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            foreach (var iface in type.FindInterfaces(filter, null))
            {
                var arguments = iface.GetGenericArguments();

                // Found interface, inner elements type is "inner"
                if (arguments.Length == 1)
                {
                    inner = arguments[0];

                    // Search constructor compatible with IEnumerable<>
                    foreach (ConstructorInfo constructor in type.GetConstructors())
                    {
                        var parameters = constructor.GetParameters();

                        if (parameters.Length != 1)
                            continue;

                        var source = parameters[0].ParameterType;

                        if (!source.IsGenericType || source.GetGenericTypeDefinition() != typeof(IEnumerable<>))
                            continue;

                        arguments = source.GetGenericArguments();

                        if (arguments.Length != 1 || inner != arguments[0])
                            continue;

                        assign = Linker.MakeAssignArray(constructor, inner);

                        return this.LinkDecoderArray(descriptor, inner, assign, parents);
                    }
                }
            }

            // Link public readable and writable instance properties
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetGetMethod() == null || property.GetSetMethod() == null ||
                    property.Attributes.HasFlag(PropertyAttributes.SpecialName))
                    continue;

                assign = Linker.MakeAssignField(property);

                if (!this.LinkDecoderField(descriptor, property.PropertyType, property.Name, assign, parents))
                    return false;
            }

            // Link public instance fields
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Attributes.HasFlag(FieldAttributes.SpecialName))
                    continue;

                assign = Linker.MakeAssignField(field);

                if (!this.LinkDecoderField(descriptor, field.FieldType, field.Name, assign, parents))
                    return false;
            }

            return true;
        }

        private bool LinkDecoderArray<TEntity>(IDecoderDescriptor<TEntity> descriptor, Type type, object assign,
            IDictionary<Type, object> parents)
        {
            if (parents.TryGetValue(type, out var recurse))
            {
                Resolver
                    .FindMethod<Func<IDecoderDescriptor<TEntity>, DecodeAssign<TEntity, IEnumerable<object>>,
                        IDecoderDescriptor<object>, IDecoderDescriptor<object>>>((d, a, p) => d.HasItems(a, p), null,
                        new[] {type})
                    .Invoke(descriptor, new[] {assign, recurse});
            }
            else
            {
                recurse = Resolver
                    .FindMethod<Func<IDecoderDescriptor<TEntity>, DecodeAssign<TEntity, IEnumerable<object>>,
                        IDecoderDescriptor<object>>>((d, a) => d.HasItems(a), null, new[] {type})
                    .Invoke(descriptor, new[] {assign});

                var result = Resolver
                    .FindMethod<Func<Linker, IDecoderDescriptor<object>, Dictionary<Type, object>, bool>>(
                        (l, d, p) => l.LinkDecoder(d, p), null, new[] {type})
                    .Invoke(this, new object[] {recurse, parents});

                if (!(result is bool))
                    throw new InvalidOperationException("internal error");

                if (!(bool) result)
                    return false;
            }

            return true;
        }

        private bool LinkDecoderField<TEntity>(IDecoderDescriptor<TEntity> descriptor, Type type, string name,
            object assign, IDictionary<Type, object> parents)
        {
            if (parents.TryGetValue(type, out var recurse))
            {
                Resolver
                    .FindMethod<Func<IDecoderDescriptor<TEntity>, string, DecodeAssign<TEntity, object>,
                        IDecoderDescriptor<object>, IDecoderDescriptor<object>>>((d, n, a, p) => d.HasField(n, a, p),
                        null, new[] {type})
                    .Invoke(descriptor, new object[] {name, assign, recurse});
            }
            else
            {
                recurse = Resolver
                    .FindMethod<Func<IDecoderDescriptor<TEntity>, string, DecodeAssign<TEntity, object>,
                        IDecoderDescriptor<object>>>((d, n, a) => d.HasField(n, a), null, new[] {type})
                    .Invoke(descriptor, new object[] {name, assign});

                var result = Resolver
                    .FindMethod<Func<Linker, IDecoderDescriptor<object>, Dictionary<Type, object>, bool>>(
                        (l, d, p) => l.LinkDecoder(d, p), null, new[] {type})
                    .Invoke(this, new object[] {recurse, parents});

                if (!(result is bool))
                    throw new InvalidOperationException("internal error");

                if (!(bool) result)
                    return false;
            }

            return true;
        }

        private bool LinkEncoder<T>(IEncoderDescriptor<T> descriptor, Dictionary<Type, object> parents)
        {
            object access;
            Type inner;

            try
            {
                descriptor.IsValue();

                return true;
            }
            catch (InvalidCastException) // FIXME: hack
            {
            }

            var type = typeof(T);

            parents = new Dictionary<Type, object>(parents) {[type] = descriptor};

            // Target type is an array
            if (type.IsArray)
            {
                inner = type.GetElementType();
                access = Linker.MakeAccessArray(inner);

                return this.LinkEncoderArray(descriptor, inner, access, parents);
            }

            // Target type implements IEnumerable<> interface
            var filter = new TypeFilter((t, c) => t.IsGenericType && t.GetGenericTypeDefinition() == typeof(IEnumerable<>));

            foreach (Type iface in type.FindInterfaces(filter, null))
            {
                var arguments = iface.GetGenericArguments();

                // Found interface, inner elements type is "inner"
                if (arguments.Length == 1)
                {
                    inner = arguments[0];
                    access = Linker.MakeAccessArray(inner);

                    return this.LinkEncoderArray(descriptor, inner, access, parents);
                }
            }

            // Link public readable and writable instance properties
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.Public))
            {
                if (property.GetGetMethod() == null || property.GetSetMethod() == null ||
                    property.Attributes.HasFlag(PropertyAttributes.SpecialName))
                    continue;

                access = Linker.MakeAccessField(property);

                if (!this.LinkEncoderField(descriptor, property.PropertyType, property.Name, access, parents))
                    return false;
            }

            // Link public instance fields
            foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.Public))
            {
                if (field.Attributes.HasFlag(FieldAttributes.SpecialName))
                    continue;

                access = Linker.MakeAccessField(field);

                if (!this.LinkEncoderField(descriptor, field.FieldType, field.Name, access, parents))
                    return false;
            }

            return true;
        }

        private bool LinkEncoderArray<T>(IEncoderDescriptor<T> descriptor, Type type, object access,
            IDictionary<Type, object> parents)
        {
            if (parents.TryGetValue(type, out var recurse))
            {
                Resolver
                    .FindMethod<Func<IEncoderDescriptor<T>, Func<T, IEnumerable<object>>, IEncoderDescriptor<object>,
                        IEncoderDescriptor<object>>>((d, a, p) => d.HasItems(a, p), null, new[] {type})
                    .Invoke(descriptor, new[] {access, recurse});
            }
            else
            {
                recurse = Resolver
                    .FindMethod<Func<IEncoderDescriptor<T>, Func<T, IEnumerable<object>>, IEncoderDescriptor<object>>>(
                        (d, a) => d.HasItems(a), null, new[] {type})
                    .Invoke(descriptor, new[] {access});

                var result = Resolver
                    .FindMethod<Func<Linker, IEncoderDescriptor<object>, Dictionary<Type, object>, bool>>(
                        (l, d, p) => l.LinkEncoder(d, p), null, new[] {type})
                    .Invoke(this, new object[] {recurse, parents});

                if (!(result is bool))
                    throw new InvalidOperationException("internal error");

                if (!(bool) result)
                    return false;
            }

            return true;
        }

        private bool LinkEncoderField<T>(IEncoderDescriptor<T> descriptor, Type type, string name, object access,
            IDictionary<Type, object> parents)
        {
            if (parents.TryGetValue(type, out var recurse))
            {
                Resolver
                    .FindMethod<Func<IEncoderDescriptor<T>, string, Func<T, object>, IEncoderDescriptor<object>,
                        IEncoderDescriptor<object>>>((d, n, a, p) => d.HasField(n, a, p), null, new[] {type})
                    .Invoke(descriptor, new object[] {name, access, recurse});
            }
            else
            {
                recurse = Resolver
                    .FindMethod<Func<IEncoderDescriptor<T>, string, Func<T, object>, IEncoderDescriptor<object>>>(
                        (d, n, a) => d.HasField(n, a), null, new[] {type})
                    .Invoke(descriptor, new object[] {name, access});

                var result = Resolver
                    .FindMethod<Func<Linker, IEncoderDescriptor<object>, Dictionary<Type, object>, bool>>(
                        (l, d, p) => l.LinkEncoder(d, p), null, new[] {type})
                    .Invoke(this, new object[] {recurse, parents});

                if (!(result is bool))
                    throw new InvalidOperationException("internal error");

                if (!(bool) result)
                    return false;
            }

            return true;
        }

        private static object MakeAccessArray(Type inner)
        {
            var enumerable = typeof(IEnumerable<>).MakeGenericType(inner);
            var method = new DynamicMethod(string.Empty, enumerable, new[] {enumerable}, inner.Module, true);
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ret);

            return method.CreateDelegate(typeof(Func<,>).MakeGenericType(enumerable, enumerable));
        }

        private static object MakeAccessField(FieldInfo field)
        {
            var method = new DynamicMethod(string.Empty, field.FieldType, new[] {field.DeclaringType}, field.Module, true);
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldfld, field);
            generator.Emit(OpCodes.Ret);

            return method.CreateDelegate(typeof(Func<,>).MakeGenericType(field.DeclaringType, field.FieldType));
        }

        private static object MakeAccessField(PropertyInfo property)
        {
            var method = new DynamicMethod(string.Empty, property.PropertyType, new[] {property.DeclaringType},
                property.Module, true);
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Call, property.GetGetMethod());
            generator.Emit(OpCodes.Ret);

            return method.CreateDelegate(typeof(Func<,>).MakeGenericType(property.DeclaringType,
                property.PropertyType));
        }

        /// <summary>
        /// Generate DecoderAssign delegate from IEnumerable to any compatible
        /// object, using a constructor taking the IEnumerable as its argument.
        /// </summary>
        /// <param name="constructor">Compatible constructor</param>
        /// <param name="inner">Inner elements type</param>
        /// <returns>DecoderAssign delegate</returns>
        private static object MakeAssignArray(ConstructorInfo constructor, Type inner)
        {
            var enumerable = typeof(IEnumerable<>).MakeGenericType(inner);
            var method = new DynamicMethod(string.Empty, null,
                new[] {constructor.DeclaringType.MakeByRefType(), enumerable}, constructor.Module, true);
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Newobj, constructor);

            if (constructor.DeclaringType.IsValueType)
                generator.Emit(OpCodes.Stobj, constructor.DeclaringType);
            else
                generator.Emit(OpCodes.Stind_Ref);

            generator.Emit(OpCodes.Ret);

            return method.CreateDelegate(
                typeof(DecodeAssign<,>).MakeGenericType(constructor.DeclaringType, enumerable));
        }

        /// <summary>
        /// Generate DecoderAssign delegate from IEnumerable to compatible array
        /// type, using Linq Enumerable.ToArray conversion.
        /// </summary>
        /// <param name="inner">Inner elements type</param>
        /// <returns>DecoderAssign delegate</returns>
        private static object MakeAssignArray(Type inner)
        {
            var converter = Resolver.FindMethod<Func<IEnumerable<object>, object[]>>((e) => e.ToArray(), null, new[] {inner});
            var enumerable = typeof(IEnumerable<>).MakeGenericType(inner);
            var method = new DynamicMethod(string.Empty, null, new[] {inner.MakeArrayType().MakeByRefType(), enumerable},
                converter.Module, true);
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);
            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Call, converter);
            generator.Emit(OpCodes.Stind_Ref);
            generator.Emit(OpCodes.Ret);

            return method.CreateDelegate(typeof(DecodeAssign<,>).MakeGenericType(inner.MakeArrayType(), enumerable));
        }

        private static object MakeAssignField(FieldInfo field)
        {
            var method = new DynamicMethod(string.Empty, null,
                new[] {field.DeclaringType.MakeByRefType(), field.FieldType},
                field.Module, true);
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);

            if (!field.DeclaringType.IsValueType)
                generator.Emit(OpCodes.Ldind_Ref);

            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Stfld, field);
            generator.Emit(OpCodes.Ret);

            return method.CreateDelegate(typeof(DecodeAssign<,>).MakeGenericType(field.DeclaringType, field.FieldType));
        }

        private static object MakeAssignField(PropertyInfo property)
        {
            var method = new DynamicMethod(string.Empty, null,
                new[] {property.DeclaringType.MakeByRefType(), property.PropertyType}, property.Module, true);
            var generator = method.GetILGenerator();

            generator.Emit(OpCodes.Ldarg_0);

            if (!property.DeclaringType.IsValueType)
                generator.Emit(OpCodes.Ldind_Ref);

            generator.Emit(OpCodes.Ldarg_1);
            generator.Emit(OpCodes.Call, property.GetSetMethod());
            generator.Emit(OpCodes.Ret);

            return method.CreateDelegate(
                typeof(DecodeAssign<,>).MakeGenericType(property.DeclaringType, property.PropertyType));
        }
    }
}