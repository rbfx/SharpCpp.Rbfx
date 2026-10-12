using System.Globalization;
using Rbfx.Examples.Common;
using Urho3D.Core;

namespace Rbfx.Examples.Desktop;

internal static class Program
{
    private static int Main()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        using var context = new Context();
        using var app = new DemoApplication(context, Path.Combine(AppContext.BaseDirectory, "Example.Desktop.log"));
        return app.Run();
    }
}
