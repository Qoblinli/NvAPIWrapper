using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using NvAPIWrapper.Native.Attributes;
using NvAPIWrapper.Native.General.Structures;
using NvAPIWrapper.Native.Interfaces;

namespace NvAPIWrapper.Native.Helpers
{
    internal static class ExtensionMethods
    {
        public static Type[] Accepts(this Delegate @delegate, int parameterIndex = 0)
        {
            Type[] types = null;
            var parameters = @delegate.GetType().GetMethod("Invoke")?.GetParameters();
            if (parameterIndex > 0)
                if (parameters?.Length >= parameterIndex)
                    types = parameters[parameterIndex - 1].GetCustomAttributes(typeof(AcceptsAttribute), true)
                        .Cast<AcceptsAttribute>()
                        .FirstOrDefault()?
                        .Types;
            if (types == null)
                if (parameters != null)
                    types = parameters.SelectMany(param => param.GetCustomAttributes(typeof(AcceptsAttribute), true))
                        .Cast<AcceptsAttribute>()
                        .FirstOrDefault()?
                        .Types;
                else
                    types = @delegate.GetType().GetCustomAttributes(typeof(AcceptsAttribute), false)
                        .Cast<AcceptsAttribute>()
                        .FirstOrDefault()?
                        .Types;
            return types ?? new Type[0];
        }

        public static IEnumerable<T> AllocateAll<T>(this IEnumerable<T> allocatableArray)
        {
            foreach (
                var allocatable in
                allocatableArray.Where(item => item.GetType().GetInterfaces().Contains(typeof(IAllocatable))))
            {
                var boxedCopy = (IAllocatable) allocatable;
                boxedCopy.Allocate();
                yield return (T) boxedCopy;
            }
        }

        public static TResult BitwiseConvert<TResult, T>(T source) where TResult : struct, IConvertible
            where T : struct, IConvertible
        {
            if (typeof(T) == typeof(TResult))
                return (TResult) (object) source;
            var sourceSize = Marshal.SizeOf(typeof(T));
            var destinationSize = Marshal.SizeOf(typeof(TResult));
            var minSize = Math.Min(sourceSize, destinationSize);
            var sourcePointer = Marshal.AllocHGlobal(sourceSize);
            Marshal.StructureToPtr(source, sourcePointer, false);
            var bytes = new byte[destinationSize];
            if (BitConverter.IsLittleEndian)
                Marshal.Copy(sourcePointer, bytes, 0, minSize);
            else
                Marshal.Copy(sourcePointer + (sourceSize - minSize), bytes, destinationSize - minSize, minSize);
            Marshal.FreeHGlobal(sourcePointer);
            var destinationPointer = Marshal.AllocHGlobal(destinationSize);
            Marshal.Copy(bytes, 0, destinationPointer, destinationSize);
            var destination = (TResult) Marshal.PtrToStructure(destinationPointer, typeof(TResult));
            Marshal.FreeHGlobal(destinationPointer);
            return destination;
        }

        public static void DisposeAll<T>(this IEnumerable<T> disposableArray)
        {
            foreach (
                var disposable in
                disposableArray.Where(
                    item =>
                        item.GetType()
                            .GetInterfaces()
                            .Any(i => (i == typeof(IDisposable)) || (i == typeof(IAllocatable)))))
                ((IDisposable) disposable).Dispose();
        }

        public static bool GetBit<T>(this T integer, int index) where T : struct, IConvertible
        {
            var bigInteger = BitwiseConvert<ulong, T>(integer);
            return (bigInteger & (1ul << index)) != 0;
        }

        // ReSharper disable once FunctionComplexityOverflow
        public static T Instantiate<T>(this Type type)
        {
            object instance = default(T);
            try
            {
                if (type.IsValueType)
                    instance = (T) Activator.CreateInstance(type);
                if (type.GetInterfaces().Any(i => (i == typeof(IInitializable)) || (i == typeof(IAllocatable))))
                    foreach (var field in type.GetRuntimeFields())
                    {
                        if (field.IsStatic || field.IsLiteral)
                            continue;
                        if (field.FieldType == typeof(StructureVersion))
                        {
                            var version =
                                type.GetCustomAttributes(typeof(StructureVersionAttribute), true)
                                    .Cast<StructureVersionAttribute>()
                                    .FirstOrDefault()?
                                    .VersionNumber;
                            field.SetValue(instance,
                                version.HasValue ? new StructureVersion(version.Value, type) : new StructureVersion());
                        }
                        else if (field.FieldType.IsArray)
                        {
                            var size =
                                field.GetCustomAttributes(typeof(MarshalAsAttribute), false)
                                    .Cast<MarshalAsAttribute>()
                                    .FirstOrDefault(attribute => attribute.Value != UnmanagedType.LPArray)?
                                    .SizeConst;
                            var arrayType = field.FieldType.GetElementType();
                            var array = Array.CreateInstance(arrayType, size ?? 0);
                            if (arrayType.IsValueType)
                                for (var i = 0; i < array.Length; i++)
                                {
                                    var obj = arrayType.Instantiate<object>();
                                    array.SetValue(obj, i);
                                }
                            field.SetValue(instance, array);
                        }
                        else if (field.FieldType.IsValueType)
                        {
                            var isByRef = field.GetCustomAttributes(typeof(MarshalAsAttribute), false)
                                .Cast<MarshalAsAttribute>()
                                .Any(attribute => attribute.Value == UnmanagedType.LPStruct);
                            if (!isByRef)
                            {
                                var value = field.FieldType.Instantiate<object>();
                                field.SetValue(instance, value);
                            }
                        }
                    }
            }
            catch
            {
                // ignored
            }
            return (T) instance;
        }

        public static T[] Repeat<T>(this T structure, int count)
        {
            return Enumerable.Range(0, count).Select(i => structure).ToArray();
        }

        public static T SetBit<T>(this T integer, int index, bool value) where T : struct, IConvertible
        {
            var bigInteger = BitwiseConvert<ulong, T>(integer);
            var mask = 1ul << index;
            var newInteger = value ? bigInteger | mask : bigInteger & ~mask;
            return BitwiseConvert<T, ulong>(newInteger);
        }
    }
}