# TODO

## Known limitations

- Windows code paths of `rbfx_csharp_add_bindings()` are untested.
- The Android, iOS and UWP example builds are not verified.
- Package mode finds the engine binding metadata in the runtime package of the target RID (`runtimes/<rid>/metadata/`). Only linux-x64 is verified locally. The other RIDs use the same staging and lookup code.
- The static variant of `Example.UserCode.Impl` (an object library for iOS and wasm) is not built.
- `Example.Common` and `Rbfx.Examples.slnx` now need the SDK.
