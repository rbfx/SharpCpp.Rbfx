using Windows.UI.Xaml.Controls;

namespace Rbfx.Examples.UwpScene;

public sealed partial class MainPage : Page
{
    public MainPage(string status)
    {
        InitializeComponent();
        SceneStatus.Text = status;
    }
}
