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

        // The example has no scene manifest and no scene delegate, so the application owns the window.
        // The UIWindowScene overload needs the scene lifecycle, which this minimal example does not use.
#pragma warning disable CA1422
        var window = new UIWindow(UIScreen.MainScreen.Bounds);
#pragma warning restore CA1422
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
