using Android.App;
using Android.OS;
using Android.Widget;
using Rbfx.Examples.Common;
using Urho3D.Core;

namespace Rbfx.Examples.Android;

[Activity(Label = "rbfx Scene", MainLauncher = true, Theme = "@android:style/Theme.Material.Light.NoActionBar", Exported = true)]
public sealed class MainActivity : Activity
{
    private Context? _context;
    private SceneSample? _sample;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Program.Main(this);
    }

    internal void CreateScene()
    {
        _context = new Context();
        _sample = new SceneSample(_context);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        var resources = Resources!;
        var padding = (int)(16 * resources.DisplayMetrics!.Density);
        var statusBarResource = resources.GetIdentifier("status_bar_height", "dimen", "android");
        var statusBarHeight = statusBarResource == 0 ? 0 : resources.GetDimensionPixelSize(statusBarResource);
        layout.SetPadding(padding, statusBarHeight + padding, padding, padding);
        var description = new TextView(this) { Text = "Create nodes in a native rbfx scene using NuGet bindings.", TextSize = 20 };
        var details = new TextView(this) { Text = _sample.DescribeLastNode(), TextSize = 18 };
        var add = new Button(this) { Text = "Add a node" };
        add.Click += (_, _) =>
        {
            _sample.AddNode();
            details.Text = _sample.DescribeLastNode();
        };
        layout.AddView(description);
        layout.AddView(add);
        layout.AddView(details);
        SetContentView(layout);
    }

    protected override void OnDestroy()
    {
        _sample?.Dispose();
        _context?.Dispose();
        base.OnDestroy();
    }
}

internal static class Program
{
    public static void Main(MainActivity activity) => activity.CreateScene();
}
