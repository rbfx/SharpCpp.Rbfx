using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using Urho3D.Interop;

namespace Urho3D.Plugins;

/// <summary>
/// Managed entry points for a native application that hosts .NET. <c>Urho3D::DotNetHost</c> (Rbfx.Host) calls them.
/// Application code does not call them.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static unsafe class NativeHostEntryPoints
{
    /// <summary>
    /// Installs the script runtime, then loads the plugin assembly at the UTF-8 path into the plugin manager.
    /// See <see cref="PluginManagerExtensions.LoadManagedPlugin(PluginManager, string, string?)"/>.
    /// Returns 1 on success. On failure it calls <paramref name="writeError"/> with a UTF-8 message and returns 0.
    /// </summary>
    [RequiresUnreferencedCode("The hostfxr host loads the plugin assembly from a file. Trimmed and AOT applications attach through SharpCpp.Rbfx.NativeHost.")]
    [UnmanagedCallersOnly]
    public static int LoadPlugin(
        nint pluginManager,
        byte* assemblyPath,
        delegate* unmanaged<nint, byte*, int, void> writeError,
        nint errorState)
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

            // The engine owns the plugin manager. The wrapper only borrows it.
            var manager = PluginManager.CreateManagedWrapper(pluginManager, ownsHandle: false);
            manager.LoadManagedPlugin(path);
            return 1;
        }
        catch (Exception exception)
        {
            WriteError(writeError, errorState, exception);
            return 0;
        }
    }

    /// <summary>
    /// Installs the script runtime, then registers the plugin assembly at the UTF-8 path in the plugin manager, as
    /// <see cref="PluginManagerExtensions.RegisterManagedPlugin(PluginManager, string, string?)"/> does.
    /// The plugin is not loaded and the list of the loaded plugins does not change.
    /// Returns 1 on success. On failure it calls <paramref name="writeError"/> with a UTF-8 message and returns 0.
    /// </summary>
    [RequiresUnreferencedCode("The hostfxr host loads the plugin assembly from a file. Trimmed and AOT applications attach through SharpCpp.Rbfx.NativeHost.")]
    [UnmanagedCallersOnly]
    public static int RegisterPlugin(
        nint pluginManager,
        byte* assemblyPath,
        delegate* unmanaged<nint, byte*, int, void> writeError,
        nint errorState)
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

            // The engine owns the plugin manager. The wrapper only borrows it.
            var manager = PluginManager.CreateManagedWrapper(pluginManager, ownsHandle: false);
            manager.RegisterManagedPlugin(path);
            return 1;
        }
        catch (Exception exception)
        {
            WriteError(writeError, errorState, exception);
            return 0;
        }
    }

    private static void WriteError(delegate* unmanaged<nint, byte*, int, void> writeError, nint errorState, Exception exception)
    {
        if (writeError == null)
        {
            return;
        }

        try
        {
            var root = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
            var bytes = Encoding.UTF8.GetBytes($"{root.GetType().Name}: {root.Message}");
            fixed (byte* text = bytes)
            {
                writeError(errorState, text, bytes.Length);
            }
        }
        catch
        {
            // The native side gets no text. The return value still reports the failure.
        }
    }
}
