using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Urho3D.Interop;

namespace Urho3D.Plugins;

/// <summary>
/// The feature switch of the native host. The MSBuild property <c>SharpCppRbfxNativeHost</c> sets it
/// (eng/SharpCpp.Rbfx.NativeHost.targets). The switch is false by default.
/// The ILLink substitution (ILLink.Substitutions.xml) replaces <see cref="IsEnabled"/> with a constant.
/// With the value false, the trimmer removes <see cref="NativeHostShim.Attach"/> code and the imports of the host library.
/// An application with a native host (a head with SharpCpp.Rbfx.Host) sets the property to true.
/// </summary>
internal static class NativeHostFeature
{
    internal const string SwitchName = "SharpCpp.Rbfx.NativeHost";

    internal static bool IsEnabled => AppContext.TryGetSwitch(SwitchName, out var enabled) && enabled;
}

/// <summary>
/// Attaches the managed function table to <c>Urho3D::DotNetHost</c> in shim mode (Android, iOS, browser-wasm).
/// In shim mode the native class never starts a runtime. It reads the function pointers from this table.
/// The module initializer runs when the bindings assembly loads. User code and head code do not call this class.
/// </summary>
internal static unsafe partial class NativeHostShim
{
    private const int HostVersion = 1;

    // The layout matches SharpCppRbfxManagedHost in Urho3D/CSharp/DotNetHostShim.h.
    internal struct ManagedHost
    {
        public int Version;
        public int Size;
        public delegate* unmanaged[Cdecl]<byte*, byte*, byte*, byte*, delegate* unmanaged[Cdecl]<nint, byte*, int, void>, nint, nint> GetFunctionPointer;
        public delegate* unmanaged[Cdecl]<nint, byte*, delegate* unmanaged[Cdecl]<nint, byte*, int, void>, nint, int> LoadPlugin;
    }

    // The shim code is in the host library (SharpCpp.Rbfx.Host), not in the wrapper library.
    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.HostName, EntryPoint = "sharpcpp_rbfx_attach_managed_host")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    private static partial int AttachManagedHost(ManagedHost* host);

    [LibraryImport(SharpCpp.Rbfx.Interop.NativeLibrary.HostName, EntryPoint = "sharpcpp_rbfx_detach_managed_host")]
    [UnmanagedCallConv(CallConvs = [typeof(CallConvCdecl)])]
    internal static partial void DetachManagedHost();

    /// <summary>True after the native library accepted the table.</summary>
    internal static bool IsAttached { get; private set; }

    // The attach must run when the assembly loads, before any head code calls DotNetHost. So a module initializer is needed.
#pragma warning disable CA2255
    [ModuleInitializer]
#pragma warning restore CA2255
    internal static void Attach()
    {
        // A NuGet-only application has no host library. With the switch false, the trimmer removes the rest of this method and the imports.
        if (!NativeHostFeature.IsEnabled)
        {
            return;
        }

        if (!OperatingSystem.IsAndroid() && !OperatingSystem.IsIOS() && !OperatingSystem.IsBrowser())
        {
            return;
        }

        try
        {
            ScriptRuntime.EnsureInstalled();
            var host = new ManagedHost
            {
                Version = HostVersion,
                Size = sizeof(ManagedHost),
                GetFunctionPointer = &ResolveEntry,
                LoadPlugin = &LoadPluginEntry,
            };
            IsAttached = AttachManagedHost(&host) == 1;
        }
        catch (Exception exception) when (exception is EntryPointNotFoundException or DllNotFoundException)
        {
            // The host library has no shim, or the application does not load it. The table stays empty and DotNetHost::Initialize reports "not attached".
        }
    }

    /// <summary>
    /// Finds the function pointer of an <see cref="UnmanagedCallersOnlyAttribute"/> method.
    /// Returns 0 and an error text when the method is not found or not valid.
    /// </summary>
    internal static nint Resolve(string assemblyPath, string typeName, string methodName, string? delegateTypeName, out string error)
    {
        error = string.Empty;
        try
        {
            if (!string.IsNullOrEmpty(delegateTypeName))
            {
                error = "delegateTypeName is not supported by the DotNetHost shim.";
                return 0;
            }

            var assembly = FindAssembly(assemblyPath);
            var type = FindType(assembly, typeName);
            if (type is null)
            {
                error = $"The type '{typeName}' is not found in the assembly '{assembly.GetName().Name}'.";
                return 0;
            }

            var method = type.GetMethod(methodName, BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (method is null)
            {
                error = $"The static method '{methodName}' is not found in the type '{type.FullName}'.";
                return 0;
            }

            if (method.GetCustomAttribute<UnmanagedCallersOnlyAttribute>() is null)
            {
                error = $"The method '{type.FullName}.{methodName}' does not have UnmanagedCallersOnlyAttribute.";
                return 0;
            }

            return method.MethodHandle.GetFunctionPointer();
        }
        catch (Exception exception)
        {
            error = Describe(exception);
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static nint ResolveEntry(
        byte* assemblyPath,
        byte* typeName,
        byte* methodName,
        byte* delegateTypeName,
        delegate* unmanaged[Cdecl]<nint, byte*, int, void> writeError,
        nint state)
    {
        try
        {
            var pointer = Resolve(
                Marshal.PtrToStringUTF8((nint)assemblyPath) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)typeName) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)methodName) ?? string.Empty,
                Marshal.PtrToStringUTF8((nint)delegateTypeName),
                out var error);
            if (pointer == 0)
            {
                WriteError(writeError, state, error);
            }

            return pointer;
        }
        catch (Exception exception)
        {
            WriteError(writeError, state, Describe(exception));
            return 0;
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static int LoadPluginEntry(
        nint pluginManager,
        byte* assemblyPath,
        delegate* unmanaged[Cdecl]<nint, byte*, int, void> writeError,
        nint state)
    {
        try
        {
            ScriptRuntime.EnsureInstalled();
            if (pluginManager == 0)
            {
                throw new ArgumentNullException(nameof(pluginManager));
            }

            var path = Marshal.PtrToStringUTF8((nint)assemblyPath);
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("The plugin assembly path is empty.", nameof(assemblyPath));
            }

            var assembly = FindAssembly(path);
            // The engine owns the plugin manager. The wrapper only borrows it.
            var manager = PluginManager.CreateManagedWrapper(pluginManager, ownsHandle: false);
            manager.LoadManagedPlugin(assembly);
            return 1;
        }
        catch (Exception exception)
        {
            WriteError(writeError, state, Describe(exception));
            return 0;
        }
    }

    // The assembly name is the file stem of the path. Loaded assemblies come first, then Assembly.Load.
    private static Assembly FindAssembly(string assemblyPath)
    {
        // Only the .dll extension is removed, because an assembly name can have dots.
        var name = Path.GetFileName(assemblyPath);
        if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
        {
            name = name[..^4];
        }

        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("The assembly path is empty.", nameof(assemblyPath));
        }

        foreach (var loaded in AppDomain.CurrentDomain.GetAssemblies())
        {
            if (string.Equals(loaded.GetName().Name, name, StringComparison.Ordinal))
            {
                return loaded;
            }
        }

        return Assembly.Load(new AssemblyName(name));
    }

    // A type name can have the form "Namespace.Type, Assembly". The assembly part is already resolved.
    private static Type? FindType(Assembly assembly, string typeName)
    {
        var comma = typeName.IndexOf(',', StringComparison.Ordinal);
        var name = comma >= 0 ? typeName[..comma].Trim() : typeName;
        return assembly.GetType(name, throwOnError: false);
    }

    private static string Describe(Exception exception)
    {
        var root = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        return $"{root.GetType().Name}: {root.Message}";
    }

    private static void WriteError(delegate* unmanaged[Cdecl]<nint, byte*, int, void> writeError, nint state, string message)
    {
        if (writeError == null)
        {
            return;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(message);
            fixed (byte* text = bytes)
            {
                writeError(state, text, bytes.Length);
            }
        }
        catch
        {
            // The native side gets no text. The return value still reports the failure.
        }
    }
}
