# Harmony Patches for Apple Silicon native & Rosetta

This package contains patches for Harmony to work on Apple Silicon.

The patch includes:

- Adds support for Apple Silicon's `W^X` memory protection with `pthread_jit_write_protect_np`.
- Fixes Rosetta2 detection in Harmony ([pardeike/Harmony#517])

[pardeike/Harmony#517]: https://github.com/pardeike/Harmony/issues/517

## Usage

### As a VPM Package for Unity

To use this package as a VPM package, you first need to add the VPM repository that contains this package.
You can add anatawa12's VPM repository by clicking [here][anatawa12's VPM repository] 
or by manually adding `https://vpm.anatawa12.com/vpm.json` to your VPM configuration.

And then you can install `Harmony Patches for Apple Silicon native & Rosetta` (`com.anatawa12.apple-silicone-harmony`)
with your VPM client like ALCOM.

[anatawa12's VPM repository]: https://vpm.anatawa12.com/add-repo

### As a standalone library

You can use this library as a `.netstandard2.1` library. You can download the compiled library from the releases page, or build from the source code. (See section below.)

You can call `Anatawa12.HarmonyAppleSilicon.Patcher.Patch()` to apply the patches for Harmony loaded to the current AppDomain,
or use other overloads to apply patches in other ways.

## Limitations

- W^X patch is only available for mono runtime.
  It does not support .NET Core or .NET 5+ runtime.
  This is just because my original intention was to support Unity, and Unity uses mono runtime.
  It's welcome for PR to support .NET Core or .NET 5+ runtime.

- W^X patch only works when Hardened Runtime is disabled.

  This is because we need to use `pthread_jit_write_protect_np` to apply the patches,
  However, `pthread_jit_write_protect_np` is not available when Hardened Runtime is enabled.

- For Unity usage, if some user of `Harmony` is patching on `[InitializeOnLoad]` method, it can be ran before this patch is applied.
  Please consider move to `EditorApplication.delayCall`, `EditorApplication.update`, or `AssemblyReloadEvents.afterAssemblyReload` to run Harmony patches after this patch for Harmony is applied.
- For any usage, you have to call `Patcher.Patch()` before Harmony is loaded / used in any way.
  Especially, Rosetta2 detection patch cannot be applied after Harmony is loaded.

## Project structure

This project is consists of three parts:

- `Native~` - The native library that actually applies the patches instead of C# code.
- `Patcher` - The C# code that applies the patches.
- `UnityApplier` - The Unity-specific code that applies the patches when Unity starts or Unity reloads assemblies.

## Building / Development

At first, this project uses symbolic links to link the `Native~` directory to the `UnityApplier` directory.
If you're on Windows, you need extra care to create symbolic links.

### Building for Unity

To build this package for Unity, you need to compile the native library written in Rust so you need to have Rust installed.

And then run `cargo build --release` in the `Native~` directory to build the native library.

### Building for .NET

To build this package for .NET, you need to have both the .NET SDK and the Rust toolchain installed.

Run `cargo build --release` in the `Native~` directory to build the native library, and then run `dotnet build` in the `Build` directory to build the C# code.

## Behavior Changes this patch makes for CoreMod and Harmony

Basically, this patch does not change the behavior of Harmony or CoreMod, but it adds some features to support Apple Silicon.

However, we need to change the behavior of CoreMod a little bit to support Apple Silicon's `W^X` memory protection.

- The `MakeWritable`, `MakeExecutable`, `FlushICache` methods on `IDetourNativePlatform` become no-op on Apple Silicon native.
  Therefore, you cannot write code to edit code memory directly. This is limitation of `W^X` memory protection.
- `IDetourNativePlatform.Apply` will manage the memory protection, and flush the instruction cache.
  Therefore, you won't receive memory protection error when you apply a detour on Apple Silicon native.
- The `IDetourNativePlatform.Copy` method will not be supported.
  Please use `IDetourNativePlatform.Apply` instead to copy a detour.

## License

Copyright (c) 2025 anatawa12

Mozilla Public License 2.0. See [LICENSE.txt](LICENSE.txt) for details.
