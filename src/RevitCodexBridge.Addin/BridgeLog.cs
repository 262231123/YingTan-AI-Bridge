namespace RevitCodexBridge.Addin;

internal static class BridgeLog
{
    private static readonly object Sync = new();

    public static string LogPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "RevitCodexBridge",
        "bridge.log");

    public static void Info(string message)
    {
        Write("INFO", message, null);
    }

    public static void Error(string message, Exception exception)
    {
        Write("ERROR", message, exception);
    }

    private static void Write(string level, string message, Exception? exception)
    {
        try
        {
            var directory = Path.GetDirectoryName(LogPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var line = $"[{DateTimeOffset.Now:O}] {level} {message}";
            if (exception is not null)
            {
                line += Environment.NewLine + exception;
            }

            lock (Sync)
            {
                File.AppendAllText(LogPath, line + Environment.NewLine, EncodingUtf8NoBom.Value);
            }
        }
        catch
        {
            // Logging must never break Revit startup or command execution.
        }
    }

    private static class EncodingUtf8NoBom
    {
        public static readonly System.Text.Encoding Value = new System.Text.UTF8Encoding(false);
    }
}

