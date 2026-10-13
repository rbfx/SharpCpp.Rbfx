# TODO

## Known limitations

- Windows code paths of `rbfx_csharp_add_usercode()` are untested.
- The multi-header umbrella path of `rbfx_csharp_add_usercode()` is untested.
- `FindNetHost` has no musl RID handling.
- `HostInfo::Describe()` returns a reference. A by-value `ea::string` return does not compile in the generated C#.
- RmlUI data model string values show garbage in the generated bindings (`Rml.Variant.Assign` of `Ea.String`).
- The `Node.Rotation` getter returns garbage.
- The Android, iOS and UWP example builds are not verified.
- `Example.Common` and `Rbfx.Examples.slnx` now need the SDK.
