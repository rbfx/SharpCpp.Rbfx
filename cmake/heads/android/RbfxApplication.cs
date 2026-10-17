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
public sealed class RbfxApplication : Application
{
    public RbfxApplication(IntPtr handle, JniHandleOwnership ownership)
        : base(handle, ownership)
    {
    }

    public override void OnCreate()
    {
        base.OnCreate();
        RuntimeHelpers.RunModuleConstructor(typeof(Urho3D.Plugins.NativeHostModule).Module.ModuleHandle);
        Program.RunStartup();
    }
}
