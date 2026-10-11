using Urho3D.Core;
using Urho3D.Math;
using Urho3D.Scene;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;

namespace Rbfx.Examples.UwpScene;

sealed partial class App : Application
{
    private Context? _context;
    private Scene? _scene;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _context = new Context();
        _scene = new Scene(_context);
        var node = _scene.CreateChild(0u);
        node.Position = new Vector3(3, 4, 0);

        Window.Current.Content = new MainPage($"Created an rbfx scene node. Its distance from the origin is {node.Position.Length:F1}.");
        Window.Current.Activate();
    }
}
