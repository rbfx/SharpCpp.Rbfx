using Android.App;
using Android.OS;
using Android.Widget;
using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;

namespace Rbfx.Examples.AndroidScene;

[Activity(Label = "rbfx Scene", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private Context? _context;
    private Scene? _scene;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Program.Main(this);
    }

    internal void CreateScene()
    {
        _context = new Context();
        _scene = new Scene(_context);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        layout.SetPadding(32, 32, 32, 32);
        var description = new TextView(this) { Text = "Create nodes in a native rbfx scene using NuGet bindings.", TextSize = 20 };
        var details = new TextView(this) { Text = "The scene is empty.", TextSize = 18 };
        var add = new Button(this) { Text = "Add a node" };
        var count = 0u;
        add.Click += (_, _) =>
        {
            var node = _scene.CreateChild(++count);
            node.Position = new Vector3(count, count * 2, 0);
            details.Text = $"Nodes: {count}\nLast node position: ({count}, {count * 2}, 0)\nDistance from origin: {node.Position.Length:F2}";
        };
        layout.AddView(description);
        layout.AddView(add);
        layout.AddView(details);
        SetContentView(layout);
    }

    protected override void OnDestroy()
    {
        _scene?.Dispose();
        _context?.Dispose();
        base.OnDestroy();
    }
}

internal static class Program
{
    public static void Main(MainActivity activity) => activity.CreateScene();
}
