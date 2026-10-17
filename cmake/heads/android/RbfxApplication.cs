using System;
using System.Runtime.CompilerServices;
using Android.App;
using Android.Runtime;

namespace Rbfx.Head;

/// <summary>
/// The Android application class of the head. It runs before the activity loads the native libraries
/// and starts SDL_main. It runs the module initializer of SharpCpp.Rbfx.NativeHost. The initializer
/// attaches the managed function table to the host library, so DotNetHost::Initialize in the native
/// application finds the runtime.
/// </summary>
[Application]
public sealed partial class RbfxApplication : Application
{
    public RbfxApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    /// <summary>
    /// Runs in OnCreate after the module initializer of the native host and before the activity starts the native
    /// code. The engine Context does not exist yet: do not use engine objects. A partial class of the head can
    /// implement it (HEAD_SOURCES).
    /// </summary>
    static partial void OnManagedStartup();

    // There is no OnManagedShutdown hook on Android. Application.OnTerminate runs only on emulated devices, and the
    // system ends the process without a managed callback.

    public override void OnCreate()
    {
        base.OnCreate();
        RuntimeHelpers.RunModuleConstructor(typeof(Urho3D.Plugins.NativeHostModule).Module.ModuleHandle);
        OnManagedStartup();
    }
}
