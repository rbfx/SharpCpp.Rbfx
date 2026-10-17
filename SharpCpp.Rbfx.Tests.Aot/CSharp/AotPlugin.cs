using Ea;
using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.IO;
using Urho3D.Math;
using Urho3D.Plugins;

namespace AotApp;

/// <summary>
/// The plugin of the NativeAOT head test. It registers no managed types. It logs three updates and then stops the engine.
/// </summary>
public partial class AotPlugin : ManagedPlugin
{
    private const int UpdateCount = 3;
    private int _updates;

    public AotPlugin(Context context)
        : base(context)
    {
    }

    protected override void Load()
    {
        base.Load();
        Log.Info("AotApp: loaded");
        SubscribeToEvent(CoreEvents.Update, OnUpdate);
    }

    private void OnUpdate(Object sender, StringHash eventType, UnorderedMap<StringHash, Variant> eventData)
    {
        if (_updates >= UpdateCount)
        {
            return;
        }

        _updates++;
        Log.Info($"AotApp: update {_updates}");
        if (_updates == UpdateCount)
        {
            GetSubsystem<Engine>()?.Exit();
        }
    }
}
