using System;
using System.Reflection;
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
                || !Enum.TryParse(platformType, "ARN", out var armPlatform))
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
    }
}