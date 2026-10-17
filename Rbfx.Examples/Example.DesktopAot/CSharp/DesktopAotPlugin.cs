#nullable enable

using Ea;
using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.Graphics;
using Urho3D.IO;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

namespace DesktopAot;

/// <summary>
/// The plugin of the example. It creates a scene with a <see cref="Spinner"/> node. In a headless run (no Graphics
/// subsystem) it stops the engine after three updates. Otherwise it runs until the window closes.
/// </summary>
public partial class DesktopAotPlugin : ManagedPlugin
{
    private const int HeadlessUpdates = 3;
    private int _updates;
    private Scene? _scene;

    public DesktopAotPlugin(Context context)
        : base(context)
    {
    }

    protected override void Load()
    {
        base.Load();
        Log.Info("DesktopAot: loaded");
        SubscribeToEvent(CoreEvents.Update, OnUpdate);

        // GetContext returns a borrowed wrapper. An owning wrapper would keep the engine alive (see ManagedPlugin).
        _scene = new Scene(GetContext());
        _scene.CreateChild("Spinner")!.CreateComponent(Spinner.GetTypeStatic());
    }

    protected override void Unload()
    {
        _scene?.Dispose();
        _scene = null;
        base.Unload();
    }

    private void OnUpdate(Object sender, StringHash eventType, UnorderedMap<StringHash, Variant> eventData)
    {
        _updates++;
        if (_updates <= HeadlessUpdates)
        {
            Log.Info($"DesktopAot: update {_updates}");
        }

        if (_updates >= HeadlessUpdates && GetSubsystem<Graphics>() == null)
        {
            GetSubsystem<Engine>()?.Exit();
        }
    }
}
