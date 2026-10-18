using Urho3D.Core;
using Urho3D.Plugins;

namespace RbfxNativeHost;

/// <summary>
/// The plugin of this assembly. The C++ application loads it with <c>DotNetHost::LoadPlugin</c>.
/// <see cref="ManagedPlugin"/> registers <see cref="Spinner"/> and its attributes on load.
/// </summary>
public partial class NativeHostPlugin : ManagedPlugin
{
    public NativeHostPlugin(Context context)
        : base(context)
    {
    }
}
