using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Anatawa12.AppleSiliconHarmony
{
    internal static class RedirectorHelpers
    {
        public static void ReplaceMethod(MethodBase fromMethod, MethodBase toMethod)
        {
            if (fromMethod == null) throw new ArgumentNullException(nameof(fromMethod));
            if (toMethod == null) throw new ArgumentNullException(nameof(toMethod));

            Pin(fromMethod);
            Pin(toMethod);

            var fromStart = GetNativeStart(fromMethod);
            var toStart = GetNativeStart(toMethod);

            RedirectFunction(fromStart, toStart);
        }

        [DllImport("apple_silicon_harmony_native", EntryPoint = "redirect_and_clear_cache")]
        public static extern void RedirectFunction(IntPtr from, IntPtr to);

        private static Dictionary<MethodBase, RuntimeMethodHandle> PinnedHandles = new();

        private static IntPtr GetNativeStart(MethodBase method) => PinnedHandles.TryGetValue(method, out var handle)
            ? handle.GetFunctionPointer()
            : GetMethodHandle(method).GetFunctionPointer();

        private static void Pin(MethodBase method)
        {
            var handle = GetMethodHandle(method);
            PinnedHandles[method] = handle;
            DisableInlining(handle);
            
            var declaringType = method.DeclaringType;
            if ((object) declaringType != null && declaringType.IsGenericType)
                RuntimeHelpers.PrepareMethod(handle, Array.ConvertAll(declaringType.GetGenericArguments(), type => type.TypeHandle));
            else
                RuntimeHelpers.PrepareMethod(handle);
        }

        private static readonly MethodInfo DynamicMethodCreateDynMethod = typeof (DynamicMethod).GetMethod("CreateDynMethod", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly FieldInfo DynamicMethodMhandle = typeof (DynamicMethod).GetField("mhandle", BindingFlags.Instance | BindingFlags.NonPublic);

        private static RuntimeMethodHandle GetMethodHandle(MethodBase method)
        {
            if (method is DynamicMethod)
            {
                DynamicMethodCreateDynMethod?.Invoke(method, Array.Empty<object>());
                if (DynamicMethodMhandle != null)
                    return (RuntimeMethodHandle) DynamicMethodMhandle.GetValue(method);
            }
            return method.MethodHandle;
        }

        private static unsafe void DisableInlining(RuntimeMethodHandle handle)
        {
            ushort* numPtr = (ushort*) ((ulong) (long) handle.Value + 2UL);
            int num = (ushort) (*numPtr | 8U);
            *numPtr = (ushort) num;
        }
    }
}