namespace Rbfx.Head;

/// <summary>
/// The hooks of the head. Every head declares this class with the same partial methods, so one HEAD_SOURCES file
/// compiles on every platform. A partial class of the head can implement the hooks.
/// </summary>
internal static partial class Program
{
    /// <summary>
    /// Runs in RbfxApplication.OnCreate after the module initializer of the native host and before the activity starts
    /// the native code. The engine Context does not exist yet: do not use engine objects.
    /// </summary>
    static partial void OnManagedStartup();

    /// <summary>
    /// Declared for the same class on every platform. This head never calls it: Application.OnTerminate runs only on
    /// emulated devices, and the system ends the process without a managed callback.
    /// </summary>
    static partial void OnManagedShutdown();

    /// <summary>Calls <see cref="OnManagedStartup"/> for <c>RbfxApplication</c>.</summary>
    internal static void RunStartup() => OnManagedStartup();
}
