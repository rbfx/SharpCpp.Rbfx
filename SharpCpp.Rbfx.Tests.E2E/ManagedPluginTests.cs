using Urho3D.Core;
using Urho3D.Plugins;

using Xunit;

namespace SharpCpp.Rbfx.Tests.E2E;

public sealed class ManagedPluginTests
{
    [Fact]
    public void Managed_plugin_overrides_run_once_in_lifecycle_order()
    {
        using var context = new Context();
        var plugin = new RecordingPlugin(context);

        Assert.False(plugin.IsLoaded);
        Assert.False(plugin.IsStarted);
        Assert.Empty(plugin.Events);

        plugin.LoadPlugin();
        Assert.True(plugin.IsLoaded);
        plugin.StartApplication(true);
        Assert.True(plugin.IsStarted);
        plugin.StopApplication();
        Assert.False(plugin.IsStarted);
        plugin.UnloadPlugin();
        Assert.False(plugin.IsLoaded);

        Assert.Equal(new[] { "Load", "Start(True)", "Stop", "Unload" }, plugin.Events);

        plugin.Dispose();
        Assert.True(plugin.IsClosed);
        Assert.Equal(1, plugin.DisposeCount);
    }
}

public partial class RecordingPlugin : Plugin
{
    public RecordingPlugin(Context context)
        : base(context)
    {
    }

    public List<string> Events { get; } = new();

    public int DisposeCount { get; private set; }

    protected override void Load() => Events.Add("Load");

    protected override void Unload() => Events.Add("Unload");

    protected override void Start(bool isMain) => Events.Add($"Start({isMain})");

    protected override void Stop() => Events.Add("Stop");

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            DisposeCount++;
        }

        base.Dispose(disposing);
    }
}
