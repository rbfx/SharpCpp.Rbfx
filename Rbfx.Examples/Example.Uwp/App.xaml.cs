using Urho3D.Core;
using Rbfx.Examples.Common;
using Windows.ApplicationModel.Activation;
using Windows.UI.Xaml;

namespace Rbfx.Examples.Uwp;

sealed partial class App : Application
{
    private Context? _context;
    private SceneSample? _sample;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _context = new Context();
        _sample = new SceneSample(_context);

        Window.Current.Content = new MainPage(_sample.DescribeLastNode());
        Window.Current.Activate();
    }
}
