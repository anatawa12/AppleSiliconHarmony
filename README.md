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

You can use contents in the `Patcher` directory as a `.netstandard2.0` library.
You can call `Anatawa12.HarmonyAppleSilicon.Patcher.Patch()` to apply the patches for Harmony loaded to the current AppDomain,
or use other overloads to apply patches in other ways.

## Project structure

This project is consists of three parts:

- `Native~` - The native library that actually applies the patches instead of C# code.
- `Patcher` - The C# code that applies the patches.
- `UnityApplier` - The Unity-specific code that applies the patches when Unity starts or Unity reloads assemblies.

## License

MIT License. See [LICENSE.txt](LICENSE.txt) for details & copyright information.
