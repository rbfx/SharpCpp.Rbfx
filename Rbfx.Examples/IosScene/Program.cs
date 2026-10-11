using CoreGraphics;
using Foundation;
using UIKit;
using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;

namespace Rbfx.Examples.IosScene;

internal static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
internal sealed class AppDelegate : UIApplicationDelegate
{
    private Context? _context;
    private Scene? _scene;

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        _context = new Context();
        _scene = new Scene(_context);
        var node = _scene.CreateChild(0u);
        node.Position = new Vector3(3, 4, 0);

        var window = new UIWindow(UIScreen.MainScreen.Bounds);
        var controller = new UIViewController
        {
            View = new UIView { BackgroundColor = UIColor.SystemBackground }
        };
        window.RootViewController = controller;
        var label = new UILabel(new CGRect(24, 120, window.Bounds.Width - 48, 120))
        {
            Lines = 0,
            Text = $"A native rbfx scene contains one node.\nIts distance from the origin is {node.Position.Length:F1}.",
            TextAlignment = UITextAlignment.Center,
            TextColor = UIColor.Label
        };
        controller.View.AddSubview(label);
        window.MakeKeyAndVisible();
        Window = window;
        return true;
    }

    public override void WillTerminate(UIApplication application)
    {
        _scene?.Dispose();
        _context?.Dispose();
    }
}
