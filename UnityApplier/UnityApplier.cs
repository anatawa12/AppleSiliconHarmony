using System;
using UnityEditor;
using UnityEngine;

namespace Anatawa12.AppleSiliconHarmony
{
    [InitializeOnLoad]
    class UnityApplier
    {
        static UnityApplier()
        {
            var patched = Patcher.TryPatch((asm, msg) =>
            {
                if (msg.EndsWith("cancelled.")) return;
                Debug.LogError($"Patcher Error patching {asm.FullName}: {msg}");
            });
            // print to editor.log
            Console.WriteLine($"AppleSiliconHarmony Patcher: Patched {patched} assemblies.");
        }
    }
}
