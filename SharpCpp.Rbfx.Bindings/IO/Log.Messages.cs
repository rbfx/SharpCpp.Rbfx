using System.Text;

namespace Urho3D.IO;

public partial class Log
{
    /// <summary>Writes an info message to the default logger.</summary>
    public static void Info(string message) => Write(LogLevel.LogInfo, message);

    /// <summary>Writes a warning message to the default logger.</summary>
    public static void Warning(string message) => Write(LogLevel.LogWarning, message);

    /// <summary>Writes an error message to the default logger.</summary>
    public static void Error(string message) => Write(LogLevel.LogError, message);

    private static void Write(LogLevel level, string message)
    {
        using var logger = GetLogger();
        logger.Write(level, Encoding.UTF8.GetBytes(message ?? string.Empty));
    }
}
