#nullable enable

using System.Linq;

using Ea;
using Urho3D.Core;
using Urho3D.Engine;
using Urho3D.IO;
using Urho3D.Math;
using Urho3D.Plugins;
using Urho3D.Scene;

namespace AotApp;

/// <summary>
/// The plugin of the NativeAOT head test. The generated type registry registers <see cref="AotSpinner"/>. The plugin
/// creates the component in a scene, writes and reads its attribute through the engine, logs three updates and then stops the engine.
/// </summary>
public partial class AotPlugin : ManagedPlugin
{
    private const int UpdateCount = 3;
    private const float SpeedValue = 2.5f;
    private const float HeadSpeedValue = 4.5f;
    private int _updates;
    private Scene? _scene;

    public AotPlugin(Context context)
        : base(context)
    {
    }

    protected override void Load()
    {
        base.Load();
        Log.Info("AotApp: loaded");
        SubscribeToEvent(CoreEvents.Update, OnUpdate);

        if (RegisteredTypes.Contains(AotSpinner.GetTypeStatic()))
        {
            Log.Info("AotApp: component registered");
        }

        // GetContext returns a borrowed wrapper. An owning wrapper would keep the engine alive (see ManagedPlugin).
        _scene = new Scene(GetContext());
        var node = _scene.CreateChild("Spinner")!;
        var component = node.CreateComponent(AotSpinner.GetTypeStatic());
        using var name = new Ea.String("Speed");
        using (var value = new Variant(SpeedValue))
        {
            component.SetAttribute(name, value);
        }

        using var read = component.GetAttribute(name);
        var managed = component is AotSpinner spinner ? spinner.Speed : float.NaN;
        if (read.Float == SpeedValue && managed == SpeedValue)
        {
            Log.Info($"AotApp: attribute {read.Float}");
        }
        else
        {
            Log.Error($"AotApp: attribute mismatch (engine {read.Float}, managed {managed})");
        }

        CreateHeadComponent();
    }

    // The component type AotHeadSpinner lives in the head assembly (HEAD_SOURCES). The plugin does not reference the
    // head, so it creates the component by its type name. The attribute "HeadSpeed" must round-trip through the engine.
    private void CreateHeadComponent()
    {
        var headType = new StringHash("AotHeadSpinner");
        var headNode = _scene!.CreateChild("HeadSpinner")!;
        var headComponent = headNode.CreateComponent(headType);
        if (headComponent == null)
        {
            Log.Error("AotApp: head component not registered");
            return;
        }

        using var headName = new Ea.String("HeadSpeed");
        using (var value = new Variant(HeadSpeedValue))
        {
            headComponent.SetAttribute(headName, value);
        }

        using var headRead = headComponent.GetAttribute(headName);
        if (headRead.Float == HeadSpeedValue)
        {
            Log.Info("AotApp: head component registered");
        }
        else
        {
            Log.Error($"AotApp: head attribute mismatch (engine {headRead.Float})");
        }
    }

    protected override void Unload()
    {
        _scene?.Dispose();
        _scene = null;
        base.Unload();
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
