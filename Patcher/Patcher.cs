using System;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.InteropServices;

namespace Anatawa12.AppleSiliconHarmony
{
    public static class Patcher
    {
        /// <summary>
        /// This is set to true if the Harmony assembly needs to be patched.
        /// </summary>
        public static readonly bool PatchNeeded;
        private static bool IsArmProcess => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 
                                            || RuntimeInformation.ProcessArchitecture == Architecture.Arm;

        static Patcher()
        {
            // We want to patch if macOS and 'native' architecture is arm64
            PatchNeeded = RuntimeInformation.IsOSPlatform(OSPlatform.OSX) && !IsX86FromUname();

            bool IsX86FromUname()
            {
                try
                {
                    // Check if uname returns x86_64
                    var uname = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "uname",
                        Arguments = "-m",
                        RedirectStandardOutput = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });

                    uname.WaitForExit();
                    var output = uname.StandardOutput.ReadToEnd().Trim();
                    return !output.Contains("arm") && !output.Contains("aarch");
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Patches the given assembly if it is the Harmony assembly and if patching is needed.
        /// </summary>
        /// <param name="assembly">The assembly that contains Harmony (or MonoMod).</param>
        public static bool TryPatchAssembly(Assembly assembly, Action<string> errorHandler = null)
        {
            if (assembly == null) throw new ArgumentNullException(nameof(assembly));
            if (!PatchNeeded) return false;
            errorHandler ??= _ => { };

            var patched = false;

            patched |= TryPatchCurrentPlatform(assembly, errorHandler);

            if (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 && 
                RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                patched |= TryPatchWriteXorExecute(assembly, errorHandler);
            }

            return patched;
        }

        private static bool TryPatchCurrentPlatform(Assembly assembly, Action<string> errorHandler)
        {
            var asm = assembly;

            // MonoMod.Utils.Platform is a one of the core public type of MonoMod.Common
            var platformType = asm.GetType("MonoMod.Utils.Platform");
            if (platformType == null || !platformType.IsEnum)
            {
                errorHandler("Failed to find MonoMod.Utils.Platform enum type. Patching CurrentPlatform aborted.");
                return false;
            }

            if (!Enum.TryParse(platformType, "Unknown", out var unknownPlatform)
                || !Enum.TryParse(platformType, "ARM", out var armPlatform))
            {
                errorHandler("Failed to find 'Unknown' or 'ARM' value in MonoMod.Utils.Platform enum type. Patching CurrentPlatform aborted.");
                return false;
            }
            var unknownPlatformInt = Convert.ToInt32(unknownPlatform);
            var armPlatformInt = Convert.ToInt32(armPlatform);

            var platformHelperType = asm.GetType("MonoMod.Utils.PlatformHelper");
            var currentField = platformHelperType.GetField("_current", BindingFlags.Static | BindingFlags.NonPublic);
            var currentLockedField = platformHelperType.GetField("_currentLocked", BindingFlags.Static | BindingFlags.NonPublic);
            var determinePlatformMethod = platformHelperType.GetMethod("DeterminePlatform", BindingFlags.Static | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

            // check field types
            if (currentField?.FieldType != platformType) currentField = null;
            if (currentLockedField?.FieldType != typeof(bool)) currentLockedField = null;

            if (currentField == null || currentLockedField == null || determinePlatformMethod == null)
            {
                errorHandler("Failed to find several members on MonoMod.Utils.PlatformHelper field. Patching CurrentPlatform aborted.");
                return false;
            }

            var locked = (bool)currentLockedField.GetValue(null);

            if (locked)
            {
                // Since it's not supported to change platform after first access to PlatformHelper.Current,
                // We can only verify that the current platform is correct.
                var currentPlatform = currentField.GetValue(null);
                var currentPlatformInt = Convert.ToInt32(currentField.GetValue(null));
                if (currentPlatformInt != unknownPlatformInt)
                {
                    var isArm = (currentPlatformInt & armPlatformInt) == armPlatformInt;
                    if (isArm != IsArmProcess)
                    {
                        errorHandler($"Current platform is locked to {currentPlatform}, but the process is {(IsArmProcess ? "ARM" : "not ARM")}. Patching CurrentPlatform aborted.");
                        return false;
                    }
                }
            }
            else
            {
                // If the platform is not locked, we can change it.
                // We perform the platform detection, and fix isARM flag if needed.
                var currentPlatform = currentField.GetValue(null);
                var currentPlatformInt = Convert.ToInt32(currentField.GetValue(null));

                if (currentPlatformInt == unknownPlatformInt)
                {
                    // If the current platform is unknown, we need to determine it first.
                    determinePlatformMethod.Invoke(null, Array.Empty<object>());
                    currentPlatform = currentField.GetValue(null);
                    currentPlatformInt = Convert.ToInt32(currentPlatform);
                }

                var isArm = (currentPlatformInt & armPlatformInt) == armPlatformInt;

                if (isArm != IsArmProcess)
                {
                    // MonoMod's platform detection is not matching the process architecture.
                    if (IsArmProcess)
                        currentPlatformInt |= armPlatformInt;
                    else
                        currentPlatformInt &= ~armPlatformInt;

                    currentField.SetValue(null, Enum.ToObject(platformType, currentPlatformInt));
                }
            }

            return true;
        }

        // For native Apple Silicon (arm64) on macOS.
        // We need to patch the entire MonoMod to support W^X JIT Permission
        private static bool TryPatchWriteXorExecute(Assembly assembly, Action<string> errorHandler)
        {
            var patched = false;
            patched |= TryPatchNativeARMPlatformMethods(assembly, errorHandler);
            patched |= TryPatchNativeMonoPlatformMethods(assembly, errorHandler);
            patched |= TryPatchNativeMonoPosixPlatformMethods(assembly, errorHandler);
            return patched;
        }

        private static readonly MethodInfo ApplyImplMethod = typeof(Patcher).GetMethod(nameof(ApplyImpl), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo CopyImplMethod = typeof(Patcher).GetMethod(nameof(CopyImpl), BindingFlags.NonPublic | BindingFlags.Static);

        private static bool TryPatchNativeARMPlatformMethods(Assembly assembly, Action<string> errorHandler)
        {
            if (NativeDetourDataTypeInfo.Create(assembly) is not { } nativeDetourDataTypeInfo)
            {
                errorHandler("Failed to find MonoMod.RuntimeDetour.NativeDetourData type. Patching WriteXorExecute aborted.");
                return false;
            }

            var nativePlatformType = assembly.GetType("MonoMod.RuntimeDetour.Platforms.DetourNativeARMPlatform");
            if (nativePlatformType == null)
            {
                errorHandler("Failed to find MonoMod.RuntimeDetour.Platforms.DetourNativeARMPlatform type. Patching WriteXorExecute aborted.");
                return false;
            }

            var createMethod = nativePlatformType.GetMethod("Create", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(IntPtr), typeof(IntPtr), typeof(byte?) }, null);
            var freeMethod = nativePlatformType.GetMethod("Free", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { nativeDetourDataTypeInfo.Type }, null);
            var applyMethod = nativePlatformType.GetMethod("Apply", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { nativeDetourDataTypeInfo.Type }, null);
            var copyMethod = nativePlatformType.GetMethod("Copy", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(IntPtr), typeof(IntPtr), typeof(byte) }, null);
            var flushICache = nativePlatformType.GetMethod("FlushICache", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(IntPtr), typeof(uint) }, null);

            if (createMethod == null || freeMethod == null || applyMethod == null || copyMethod == null || flushICache == null)
            {
                errorHandler("Failed to find one or more methods on MonoMod.RuntimeDetour.Platforms.DetourNativeARMPlatform. Patching WriteXorExecute aborted.");
                return false;
            }

            {
                var newCreateMethod = new DynamicMethod(
                    "_Create",
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    nativeDetourDataTypeInfo.Type,
                    new[] { nativePlatformType, typeof(IntPtr), typeof(IntPtr), typeof(byte?) },
                    nativePlatformType,
                    skipVisibility: true
                );
                var il = newCreateMethod.GetILGenerator();
                var result = il.DeclareLocal(nativeDetourDataTypeInfo.Type);
                il.Emit(OpCodes.Ldloca_S, result);
                il.Emit(OpCodes.Initobj, nativeDetourDataTypeInfo.Type);

                // set Method = from
                il.Emit(OpCodes.Ldloca_S, result);
                il.Emit(OpCodes.Ldarg_1); // from
                il.Emit(OpCodes.Stfld, nativeDetourDataTypeInfo.MethodField);

                // set Target = to
                il.Emit(OpCodes.Ldloca_S, result);
                il.Emit(OpCodes.Ldarg_2); // to
                il.Emit(OpCodes.Stfld, nativeDetourDataTypeInfo.TargetField);

                // set Type = 0
                il.Emit(OpCodes.Ldloca_S, result);
                il.Emit(OpCodes.Ldc_I4_0);
                il.Emit(OpCodes.Stfld, nativeDetourDataTypeInfo.TypeField);

                // set Size = 16
                il.Emit(OpCodes.Ldloca_S, result);
                il.Emit(OpCodes.Ldc_I4_S, 16);
                il.Emit(OpCodes.Stfld, nativeDetourDataTypeInfo.SizeField);

                // return result
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Ret);

                RedirectorHelpers.ReplaceMethod(createMethod, newCreateMethod);
            }

            {
                var newFreeMethod = new DynamicMethod(
                    "_Free",
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof(void),
                    new[] { nativePlatformType, nativeDetourDataTypeInfo.Type },
                    nativePlatformType,
                    skipVisibility: true
                );
                var il = newFreeMethod.GetILGenerator();
                
                il.Emit(OpCodes.Ret);

                RedirectorHelpers.ReplaceMethod(freeMethod, newFreeMethod);
            }

            {
                var newApplyMethod = new DynamicMethod(
                    "_Apply",
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof(void),
                    new[] { nativePlatformType, nativeDetourDataTypeInfo.Type },
                    nativePlatformType,
                    skipVisibility: true
                );
                var il = newApplyMethod.GetILGenerator();

                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldfld, nativeDetourDataTypeInfo.TypeField);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldfld, nativeDetourDataTypeInfo.MethodField);
                il.Emit(OpCodes.Ldarg_1);
                il.Emit(OpCodes.Ldfld, nativeDetourDataTypeInfo.TargetField);
                il.Emit(OpCodes.Call, ApplyImplMethod);
                il.Emit(OpCodes.Ret);

                RedirectorHelpers.ReplaceMethod(applyMethod, newApplyMethod);
            }

            {
                var newCopyMethod = new DynamicMethod(
                    "_Copy",
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof(void),
                    new[] { nativePlatformType, typeof(IntPtr), typeof(IntPtr), typeof(byte) },
                    nativePlatformType,
                    skipVisibility: true
                );
                var il = newCopyMethod.GetILGenerator();

                il.Emit(OpCodes.Call, CopyImplMethod);
                il.Emit(OpCodes.Ret);

                RedirectorHelpers.ReplaceMethod(copyMethod, newCopyMethod);
            }

            {
                var newFlushICacheMethod = new DynamicMethod(
                    "_FlushICache",
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof(void),
                    new[] { nativePlatformType, typeof(IntPtr), typeof(uint) },
                    nativePlatformType,
                    skipVisibility: true
                );
                var il = newFlushICacheMethod.GetILGenerator();

                il.Emit(OpCodes.Ret);

                RedirectorHelpers.ReplaceMethod(flushICache, newFlushICacheMethod);
            }

            return true;
        }

        private static bool TryPatchNativeMonoPlatformMethods(Assembly assembly, Action<string> errorHandler) =>
            TryPatchNativeMonoPlatformMethodsBase(assembly, errorHandler,
                "MonoMod.RuntimeDetour.Platforms.DetourNativeMonoPlatform");

        private static bool TryPatchNativeMonoPosixPlatformMethods(Assembly assembly, Action<string> errorHandler) =>
            TryPatchNativeMonoPlatformMethodsBase(assembly, errorHandler,
                "MonoMod.RuntimeDetour.Platforms.DetourNativeMonoPosixPlatform");

        private static bool TryPatchNativeMonoPlatformMethodsBase(Assembly assembly, Action<string> errorHandler, string typeName)
        {
            // make 'MakeWritable' and 'MakeExecutable' no-op since 'Apply' will perform writable and executable
            var nativePlatformType = assembly.GetType(typeName);
            if (nativePlatformType == null)
            {
                errorHandler($"Failed to find {typeName} type. Patching WriteXorExecute aborted.");
                return false;
            }

            var makeWritableMethod = nativePlatformType.GetMethod("MakeWritable", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(IntPtr), typeof(uint) }, null);
            var makeExecutableMethod = nativePlatformType.GetMethod("MakeExecutable", BindingFlags.Instance | BindingFlags.Public, null,
                new[] { typeof(IntPtr), typeof(uint) }, null);

            if (makeWritableMethod == null || makeExecutableMethod == null)
            {
                errorHandler($"Failed to find MakeWritable or MakeExecutable methods on {typeName}. Patching WriteXorExecute aborted.");
                return false;
            }

            {
                var newMakeWritableMethod = new DynamicMethod(
                    "_MakeWritable",
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof(void),
                    new[] { nativePlatformType, typeof(IntPtr), typeof(uint) },
                    nativePlatformType,
                    skipVisibility: true
                );

                var il = newMakeWritableMethod.GetILGenerator();

                il.Emit(OpCodes.Ret);

                RedirectorHelpers.ReplaceMethod(makeWritableMethod, newMakeWritableMethod);
            }

            {
                var newMakeExecutableMethod = new DynamicMethod(
                    "_MakeExecutable",
                    MethodAttributes.Public,
                    CallingConventions.Standard,
                    typeof(void),
                    new[] { nativePlatformType, typeof(IntPtr), typeof(uint) },
                    nativePlatformType,
                    skipVisibility: true
                );

                var il = newMakeExecutableMethod.GetILGenerator();

                il.Emit(OpCodes.Ret);

                RedirectorHelpers.ReplaceMethod(makeExecutableMethod, newMakeExecutableMethod);
            }

            return true;
        }

        private static void ApplyImpl(int type, IntPtr method, IntPtr target)
        {
            if (type != 0) throw new NotSupportedException($"Unknown detour type {type}");
            RedirectorHelpers.RedirectFunction(method, target);
        }

        private static void CopyImpl()
        {
            // unsupported since Harmony does not use Copy
            throw new NotSupportedException("Copy is not supported in AppleSiliconHarmony.");
        }

        private struct NativeDetourDataTypeInfo
        {
            public Type Type;
            public FieldInfo MethodField;
            public FieldInfo TargetField;
            public FieldInfo TypeField;
            public FieldInfo SizeField;

            public static NativeDetourDataTypeInfo? Create(Assembly assembly)
            {
                var type = assembly.GetType("MonoMod.RuntimeDetour.NativeDetourData");
                if (type == null) return null;
                var methodField = type.GetField("Method", BindingFlags.Instance | BindingFlags.Public);
                var targetField = type.GetField("Target", BindingFlags.Instance | BindingFlags.Public);
                var typeField = type.GetField("Type", BindingFlags.Instance | BindingFlags.Public);
                var sizeField = type.GetField("Size", BindingFlags.Instance | BindingFlags.Public);
                if (methodField == null) return null;
                if (methodField.FieldType != typeof(IntPtr)) return null;
                if (targetField == null) return null;
                if (targetField.FieldType != typeof(IntPtr)) return null;
                if (typeField == null) return null;
                if (typeField.FieldType != typeof(byte)) return null;
                if (sizeField == null) return null;
                if (sizeField.FieldType != typeof(uint)) return null;
                return new NativeDetourDataTypeInfo()
                {
                    Type = type,
                    MethodField = methodField,
                    TargetField = targetField,
                    TypeField = typeField,
                    SizeField = sizeField,
                };
            }
        }
    }
}