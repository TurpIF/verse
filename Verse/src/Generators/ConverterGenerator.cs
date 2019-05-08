using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using Verse.Resolvers;

namespace Verse.Generators
{
	internal static class ConverterGenerator
	{
		/// <summary>
		/// Create setter using compatible constructor.
		/// </summary>
		public static Func<TParameter, TEntity> CreateFromConstructor<TEntity, TParameter>(ConstructorInfo constructor)
		{
			var parameters = constructor.GetParameters();

			if (constructor.DeclaringType != typeof(TEntity))
				throw new ArgumentException($"constructor parent type is not {typeof(TEntity)}",
					nameof(constructor));

			if (parameters.Length != 1)
				throw new ArgumentException("constructor doesn't take one argument", nameof(constructor));

			if (parameters[0].ParameterType != typeof(TParameter))
				throw new ArgumentException($"constructor argument type is not {typeof(TParameter)}",
					nameof(constructor));

			var parameterTypes = new[] {typeof(TEntity).MakeByRefType(), typeof(TParameter)};
			var method = new DynamicMethod(string.Empty, null, parameterTypes, constructor.Module, true);
			var generator = method.GetILGenerator();

			generator.Emit(OpCodes.Ldarg_0);
			generator.Emit(OpCodes.Newobj, constructor);
			generator.Emit(OpCodes.Ret);

			return (Func<TParameter, TEntity>) method.CreateDelegate(typeof(Func<TParameter, TEntity>));
		}

		/// <summary>
		/// Create converter from any <see cref="IEnumerable{T}"/> elements to array target.
		/// </summary>
		/// <typeparam name="TElement">Element type</typeparam>
		/// <returns>Converter callback</returns>
		public static Func<IEnumerable<TElement>, TElement[]> CreateFromEnumerable<TElement>()
		{
			var arrayConverter = MethodResolver.Create<Func<IEnumerable<TElement>, TElement[]>>(e => e.ToArray());
			var parameterTypes = new[] {typeof(TElement[]).MakeByRefType(), typeof(IEnumerable<TElement>)};
			var method = new DynamicMethod(string.Empty, null, parameterTypes, typeof(TElement).Module, true);
			var generator = method.GetILGenerator();

			generator.Emit(OpCodes.Ldarg_0);
			generator.Emit(OpCodes.Call, arrayConverter.Method);
			generator.Emit(OpCodes.Ret);

			return (Func<IEnumerable<TElement>, TElement[]>) method.CreateDelegate(
				typeof(Func<IEnumerable<TElement>, TElement[]>));
		}
	}
}