using System.IO;
using NLog;

namespace OverTranslate.Services.Performance;

/// <summary>
/// Finds the profile for the window being translated. Reads <c>game-profiles.json</c> again only when
/// it has changed, and never throws.
/// </summary>
internal static class GameProfileStore
{
    private static readonly Logger Log = LogManager.GetCurrentClassLogger();
    private static readonly object Gate = new();
    private static string _stamp = "";
    private static GameProfiles.ParseResult _parsed = GameProfiles.None;

    public static string FilePath { get; } = Path.Combine(AppDataPaths.Root, "game-profiles.json");

    /// <summary>Folder for per-game glossary files.</summary>
    public static string GlossaryFolder { get; } = Path.Combine(AppDataPaths.Root, "glossaries");

    public static GameProfile? Find(string? processName, string? windowTitle)
    {
        try
        {
            return GameProfiles.Match(Load().Profiles, processName, windowTitle);
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Game profile lookup failed; continuing without one");
            return null;
        }
    }

    private static GameProfiles.ParseResult Load()
    {
        if (!File.Exists(FilePath)) return GameProfiles.None;

        var stamp = File.GetLastWriteTimeUtc(FilePath).Ticks.ToString();
        lock (Gate)
        {
            if (stamp == _stamp) return _parsed;

            _parsed = GameProfiles.Parse(File.ReadAllText(FilePath));
            _stamp = stamp;
            Log.Info("Game profiles loaded: {Count} usable", _parsed.Profiles.Count);
            foreach (var problem in _parsed.Problems) Log.Warn("game-profiles.json: {Problem}", problem);
            return _parsed;
        }
    }

    /// <summary>What the last read of the file complained about, for showing in the panel.</summary>
    public static IReadOnlyList<string> Problems
    {
        get
        {
            try { Load(); } catch (Exception) { /* shown as no problems; Find logs the cause */ }
            lock (Gate) return _parsed.Problems;
        }
    }

    /// <summary>Creates the file with one switched-off example if there is none, and returns its path.</summary>
    public static string EnsureFile()
    {
        try
        {
            Directory.CreateDirectory(AppDataPaths.Root);
            Directory.CreateDirectory(GlossaryFolder);
            if (!File.Exists(FilePath))
                File.WriteAllText(FilePath, Template, new System.Text.UTF8Encoding(true));
        }
        catch (Exception ex)
        {
            Log.Warn(ex, "Could not create game-profiles.json");
        }

        return FilePath;
    }

    internal const string Template =
        "{\n" +
        "  // Cấu hình riêng cho từng game. Chỉ áp dụng khi dịch MỘT CỬA SỔ (không áp dụng khi dịch cả màn hình).\n" +
        "  // Sửa file khi ứng dụng đã thoát, hoặc dừng và bắt đầu lại phiên dịch để áp dụng thay đổi.\n" +
        "  // Đổi \"enabled\" thành true để bật ví dụ bên dưới. Profile đầu tiên khớp sẽ được dùng.\n" +
        "  \"profiles\": [\n" +
        "    {\n" +
        "      \"name\": \"Ví dụ: game nhẹ\",\n" +
        "      \"enabled\": false,\n" +
        "      \"process\": \"TenGame.exe\",\n" +
        "      \"mode\": \"UltraLow\",\n" +
        "      \"staticScanMaxDelayMs\": 600,\n" +
        "      \"pauseWhenNotForeground\": true,\n" +
        "      \"lowerPriority\": true,\n" +
        "      \"glossary\": \"ten-game.txt\"\n" +
        "    }\n" +
        "  ]\n" +
        "}\n";
}
