using CoreGraphics;
using Foundation;
using Rbfx.Examples.Common;
using UIKit;
using Urho3D.Core;

namespace Rbfx.Examples.Ios;

internal static class Program
{
    private static void Main(string[] args) => UIApplication.Main(args, null, typeof(AppDelegate));
}

[Register("AppDelegate")]
internal sealed class AppDelegate : UIApplicationDelegate
{
    private Context? _context;
    private SceneSample? _sample;

    public override UIWindow? Window { get; set; }

    public override bool FinishedLaunching(UIApplication application, NSDictionary? launchOptions)
    {
        _context = new Context();
        _sample = new SceneSample(_context);

        var window = new UIWindow(UIScreen.MainScreen.Bounds);
        var controller = new UIViewController
        {
            View = new UIView { BackgroundColor = UIColor.SystemBackground }
        };
        window.RootViewController = controller;
        var label = new UILabel(new CGRect(24, 120, window.Bounds.Width - 48, 120))
        {
            Lines = 0,
            Text = _sample.DescribeLastNode(),
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
        _sample?.Dispose();
        _context?.Dispose();
    }
}
