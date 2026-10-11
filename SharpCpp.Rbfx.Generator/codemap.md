# rbfx/SharpCpp.Rbfx.Generator/

## Responsibility

The rbfx bindings generator is the **phase-1 composition root** that instantiates SharpCpp's generic pipeline components (parsing, pipeline passes, rendering, metadata export) against the concrete rbfx SDK headers. It produces the complete managed projection of the rbfx engine API: C# bindings (`.g.cs`), C++ native glue (`.g.cpp`), and a binding metadata document (`Bindings.json`). It is structurally analogous to `SharpCpp.Tool` but specialized for rbfx rather than being a general-purpose CLI.

## Design Patterns

### Pipeline Composition

`Program.Main` orchestrates the binding pipeline as an explicit sequence of stages, each consuming and transforming a shared `BindingCompilation`:

```
Parse → BootstrapPipeline → ExportFilter → Render → MetadataExport
```

This is a **staged pipeline** pattern — each stage mutates the compilation model in-place, and intermediate diagnostics are logged. There is no scheduler or pass manager; the sequence is hardcoded for rbfx's needs.

### Toolchain Discovery Strategy

`CompilerToolchainDiscovery` implements a **probe-and-fallback** pattern for obtaining correct Clang invocation flags. Given a compiler path from the SDK flags file (which may be `g++`), it:

1. Passes `SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY` to `SharpCpp.Toolchain` as the resource directory override
2. Falls back to probing the compiler (or `clang++`/`clang` on PATH) with `-print-resource-dir`
3. Discovers the target triple via `-dumpmachine`
4. Extracts the system include search path from `-E -x c++ - -v` stderr parsing
5. Filters out Clang built-in include directories that belong to a different Clang major version than the in-process `ClangSharp` assembly
6. Validates that any discovered resource directory has a usable `include/` subdirectory

This ensures libclang sees a consistent set of built-in headers even when the SDK was built with a different toolchain.

### SDK Export Gating

`ApplySdkExportFilter` implements a **dynamic symbol-based filtering** strategy. Declarations that the headers declare but the linked SDK library does not export are marked as ignored, so no wrapper code references a symbol that fails to link. The library format selects the symbol source and the scope.

**ELF and Mach-O library** (`Program.LoadExportedSymbols`): runs `nm -D --defined-only --demangle` (`--extern-only` on macOS) and matches the demangled text by name. The scope is free functions and static methods that have no inline definition (`HasNativeDefinition`). A failure to start or run `nm` logs a warning and disables the filter.

**PE library** (a Windows DLL, found by its `MZ` signature; `SdkPeExportTable.cs` and `SdkPeExportFilter.cs`): `nm` is not available on a Windows host and a DLL exports only the declarations with `URHO3D_API`, so the scope is wider.

- `SdkPeExportTable` reads the export name table in process (`System.Reflection.PortableExecutable`, `PEReader.GetSectionData` maps each RVA to a file position). `TryDecodeQualifiedName` decodes the qualified name at the start of each MSVC decorated name (`?Bar@Foo@Urho3D@@...` to `Urho3D::Foo::Bar`, `??0`/`??1` to a constructor/destructor, `??_7` to a virtual table, operator codes). It uses no native undecorate function, so the same code runs and is tested on each host system. It does not decode a name with a template, an anonymous namespace or a local scope; the filter does not examine a declaration whose name contains `<`.
- `SdkPeExportFilter.Apply` uses these rules. The match is by qualified name, as for the other formats.
  - A free function, a static method, an instance method or a declared constructor with no inline definition and no export: `SetIgnoreReason`. A pure virtual function and a synthetic default constructor are not examined.
  - An exported name with an overload that is not exported: `SdkPeExportTable.ContainsOverload` looks for the name of each record and each enum of the parameter types in the encoded type of the exported overloads. This is not a full match of the signature: it does not tell apart overloads that differ only in a built-in type, a type alias or a template.
  - A static field with no definition in the header (`DeclaredOnly`, which the parser sets from the initializer and the definition state) and no export: `SetIgnoreReason`.
  - A record with a name in `MetadataKeys.DestructorLinkDependencies` that is not exported: the wrapper cannot destroy it. The parser puts in this list the destructors without a definition that the destructor of the record references (the record, its bases, its by-value fields, and the type arguments of a template field together with their `operator()`).
  - A record that has a name in `MetadataKeys.VirtualTableLinkDependencies` that is not exported: a constructor compiled into the wrapper emits the virtual table, which references each virtual function. This applies also when the library exports the virtual table: the link reports `LNK2001` for a virtual function that such a record inherits from a base without export (`ActionState::Update`). The cause is a deduction from the link errors; no object file confirms it. The parser puts in this list the final overriders (of any access) without a definition.
  - Such a record gets `MetadataKeys.NonPublicDestructor` (no `destroy` thunk), `HasCopyConstructor = false` (no clone) and all its constructors are ignored. The record stays usable through a pointer. A function that passes such a record by value is ignored.
  - A record with a name in `MetadataKeys.DefaultConstructorLinkDependencies` that is not exported: the implicit or defaulted default constructor of the record calls a default constructor that has no definition in the header (`ParticleGraphSpan::ParticleGraphSpan` for a by-value field). The parser puts in this list those constructors of the bases and the by-value fields (recursively, only through an implicit or defaulted constructor; a template specialization is not examined) and only when the record has no user-provided default constructor. Only the constructors of the record with no parameters and without `DeclaredOnly` are ignored. The other constructors, the clone and `destroy` stay.
- `Program.ApplyPeSdkExportFilter` runs after `RbfxFlagSetLoweringPass`, because the surface passes before it clear ignore reasons. It logs the export counts and one summary line with the count for each kind.

### Namespace Flattening

`BuildNamespaceMappings` maps all `Urho3D::*` nested C++ namespaces to the single `Urho3D` C# namespace. This is a deliberate flattening: the managed projection uses a single flat namespace rather than mirroring rbfx's deep C++ namespace hierarchy.

### Output Grouping by Header

`DeriveGroupStem` maps each input header path to a structured output file naming scheme. Headers under `Urho3D/` are stripped of that prefix, and path segments become dotted suffix components. For example, `Urho3D/Core/Context.h` → `SharpCpp.Rbfx.Core.Context.g.cs`. This keeps generated files organized by their SDK subsystem origin.

## Data & Control Flow

### Startup Sequence

```
Program.Main(args)
  ├── GeneratorOptions.Parse(args)
  │     ├── Extracts --sdk-root, --flags-file, --generated-root, --project-root
  │     ├── Extracts optional --sdk-library, --log-file
  │     └── Validates all paths exist (or may-exist for generated-root)
  │
  ├── CreateLogWriter(options.LogFilePath)
  │     └── Opens StreamWriter for generator log
  │
  ├── SdkParseFlags.Load(options.FlagsFilePath)
  │     ├── Reads key=value lines from flags file
  │     ├── Separates compiler_path, language_standard, include_directory[*], define[*], clang_argument[*]
  │     ├── Filters architecture-specific defines (URHO3D_SSE, BT_USE_SSE, etc.)
  │     ├── Filters clang arguments that don't affect parsing (warning flags, arch tuning, SIMD codegen)
  │     └── Normalizes language standard to gnu++20 minimum
  │
  ├── BuildClangArguments(includeRoot, sdkFlags)
  │     ├── Sets -xc++, -std=
  │     ├── Adds -Wno-* warning suppression arguments
  │     ├── Collects -I directives from SDK flags + include root
  │     ├── Adds -D defines from SDK flags + compatibility defines (SDL_DISABLE_*, IMGUI_DISABLE_*)
  │     ├── Adds SDK additional clang arguments + SIMD disable flags (-mno-sse*, -mno-avx*; x86 targets only)
  │     ├── CompilerToolchainDiscovery.Discover(sdkFlags.CompilerPath)
  │     │     ├── Resolves toolchain compiler (prefers clang if available)
  │     │     ├── Discovers target triple via compiler -dumpmachine
  │     │     ├── Discovers parser ClangSharp major version from loaded assembly
  │     │     ├── Discovers verbose include search paths via -E -v
  │     │     ├── Checks SHARPCPP_RBFX_CLANG_RESOURCE_DIRECTORY env var for override
  │     │     ├── Discovers clang resource directory via -print-resource-dir
  │     │     ├── Validates resource directory Clang version matches parser Clang version
  │     │     └── Filters built-in include dirs that don't belong to the matched resource dir
  │     └── Appends --target=, -resource-dir=, -isystem flags from toolchain info
  │
  ├── new ClangSharpHeaderParser().Parse(request)
  │     └── Returns BindingCompilation with parsed records, functions, enums, typedefs
  │
  ├── BuildNamespaceMappings(compilation)
  │     └── Creates flat mapping: all Urho3D::Sub::Name → Urho3D
  │
  ├── BindingCompilationBootstrapPipeline.Apply(compilation, marshallerConfigs)
  │     └── Runs pipeline passes (marshaller attribute resolution, type rewriting, etc.)
  │
  ├── ApplySdkExportFilter(compilation, sdkLibraryPath)
  │     ├── PE library (Windows DLL): no action here, see ApplyPeSdkExportFilter
  │     ├── Other library: runs nm -D --defined-only --demangle on the SDK shared library
  │     ├── Cross-references parsed function names against exported symbols
  │     └── Marks unexported free functions and static methods as ignored
  │
  ├── ApplyPeSdkExportFilter(compilation, sdkLibraryPath)   (after RbfxFlagSetLoweringPass)
  │     ├── SdkPeExportTable.Load reads and decodes the export name table of a Windows DLL
  │     └── SdkPeExportFilter.Apply ignores unexported functions, methods, constructors and static
  │           fields, and removes the constructors, the clone and the destroy thunk of records
  │           that need a symbol that is not exported
  │
  ├── BindingOutputRenderer.Render(compilation, stem, groupStemFunc)
  │     └── Returns RenderedOutput collection (files grouped by header origin)
  │
  ├── BindingMetadataExporter.Export(compilation, options)
  │     └── Returns BindingMetadataDocument for downstream import
  │
  ├── Write rendered files to options.GeneratedRoot
  ├── Write Bindings.json metadata to GeneratedRoot
  └── Log summary and return exit code
```

### Logging

Dual-output logging (console + file) via `Log()` helper. Errors and exceptions are written to both streams. Warning summaries for ignored records are emitted at the end of the run.

## Integration Points

### Consumes

| Dependency | Usage |
|---|---|
| `SharpCpp.Model` | `BindingCompilation`, `RecordBinding`, `FunctionBinding`, `EnumBinding`, `MetadataKeys` |
| `SharpCpp.Metadata` | `BindingMetadataExporter`, `BindingMetadataExportOptions`, `BindingMetadataSerializer` |
| `SharpCpp.Parsing` | `ClangSharpHeaderParser`, `ClangSharpParseRequest` |
| `SharpCpp.Pipeline` | `BindingCompilationBootstrapPipeline` |
| `SharpCpp.Rendering` | `BindingOutputRenderer`, `RenderedOutput` |
| `SharpCpp.Support` | (transitive dependency for output consistency) |
| rbfx SDK headers | Parsed input (via `Urho3DAll.h` umbrella header) |
| rbfx SDK shared library | `--sdk-library` for export symbol filtering (`.so`/`.dylib`/`.dll`) |
| `urho3d-sdk-flags.txt` | Compiler path, language standard, include dirs, defines, clang args |
| `include/SharpCpp/Marshaller.hpp` | Marshaller configuration consumed by the bootstrap pipeline |

### Produces

| Artifact | Location | Consumer |
|---|---|---|
| `SharpCpp.Rbfx.*.g.cs` | `RBFX_GENERATED_ROOT/` | `SharpCpp.Rbfx.Bindings.csproj` (wildcard Compile) |
| `SharpCpp.Rbfx.*.g.cpp` | `RBFX_GENERATED_ROOT/` | `SharpCppRbfx_Native` CMake target (Unity build) |
| `SharpCpp.Rbfx.Bindings.json` | `RBFX_GENERATED_ROOT/` | `Rbfx.UserCode.Generator` (`--import-binding-metadata`) |
| `ApiManifest.tsv` | `RBFX_GENERATED_ROOT/` | CI artifact `api-manifest-<rid>`; compared across platforms |

### API Manifest

`ApiManifestWriter` writes `ApiManifest.tsv` from the final `BindingCompilation`, after all passes and filters. The file lists each declaration on one line. CI compares the files of different platforms to find API differences.

The file is UTF-8 with `\n` line ends. The lines are sorted with `StringComparer.Ordinal`. The file has no host-dependent data (no paths, source locations or USR strings). A tab or line end in a value becomes a space.

Each line has four tab-separated columns: `kind`, `key`, `details`, `state`. The `details` column has items separated by `;`.

| `kind` | `key` | `details` |
|---|---|---|
| `record` | `CppQualifiedName` | `base:<name>` for each base (sorted), then `abstract`, `polymorphic` |
| `field` | `<record CppQualifiedName>::<native field name>` | `CppTypeName`, then `static`, `readonly` |
| `function` | `<CppQualifiedName>(<parameter CppTypeName list>)`, plus ` const` for a const method | return `CppTypeName`, then `static`, `virtual`, `pure`, `constructor` |
| `enum` | `CppQualifiedName` | `UnderlyingTypeName` |
| `enumvalue` | `<enum CppQualifiedName>::<NativeName>` | `ValueExpression` |
| `alias` | C++ qualified name | target type `CppTypeName` |

The `state` column is `bound` or `ignored`. A declaration is `ignored` if it or one of its ancestors has an ignore reason. The reason text is not in the file. An `enumvalue` has the state of its enum. An `alias` is always `bound`.

### Entry Point

MSBuild builds the generator DLL through a `ProjectReference` before invoking the auxiliary CMake graph. CMake's `sharpcpp-generate-rbfx` custom command then runs that already-built DLL with positional options and records the generated-source stamp.

### Associated Configuration

- `ParseCompatibilityDefines`: SDL and ImGui header-compatibility macros that suppress platform-specific SIMD detection at parse time.
- `ParseSimdDisableArguments`: `-mno-sse*`, `-mno-avx*` flags that prevent libclang from implicitly enabling SIMD intrinsics based on host CPU detection. Added only for x86 targets (or an unknown target); other targets such as arm64 reject them.
- `ParseNeonDisableArguments`: `-U__ARM_NEON*` and `SDL_DISABLE_ARM_NEON_H` for arm targets. The pinned Clang builtin headers have no `arm_neon.h`.
- `ParseWarningDisableArguments`: `-Wno-deprecated-literal-operator` and `-Wno-pragma-once-outside-header` to suppress noisy libclang diagnostics.
- `EntryPointPrefix = "scrbfx"`: Unique prefix for native entry point symbols generated in the C++ glue.
