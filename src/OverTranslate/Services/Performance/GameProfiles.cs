using System.IO;
using System.Text.Json;
using OverTranslate.Models;

namespace OverTranslate.Services.Performance;

/// <summary>
/// What the user wants different for one game. Every field except the matching ones is optional; a
/// missing field leaves the application's own setting alone.
/// </summary>
/// <param name="Name">Shown in the performance panel and the log.</param>
/// <param name="Processes">Executable names this profile is for, with or without <c>.exe</c>, any case.</param>
/// <param name="TitleContains">When given, the window title must also contain this (any case).</param>
/// <param name="Mode">Performance level to use while this game is being translated.</param>
/// <param name="StaticScanMaxDelayMs">See <see cref="PerformanceSettings.StaticScanMaxDelayMs"/>.</param>
/// <param name="PauseWhenNotForeground">See <see cref="PerformanceSettings.PauseWhenSourceNotForeground"/>.</param>
/// <param name="LowerPriority">See <see cref="PerformanceSettings.LowerProcessPriority"/>.</param>
/// <param name="Glossary">
/// File name (no folder) in <c>glossaries\</c> next to the settings; its entries are laid over the
/// global <c>glossary.txt</c>, so the same word can mean something else in this game.
/// </param>
internal sealed record GameProfile(
    string Name,
    IReadOnlyList<string> Processes,
    string? TitleContains,
    PerformanceMode? Mode,
    int? StaticScanMaxDelayMs,
    bool? PauseWhenNotForeground,
    bool? LowerPriority,
    string? Glossary);

/// <summary>
/// Reading, matching and applying the per-game profiles. Pure: the file, the window and the clock
/// are somebody else's, so all of it can be tested.
/// </summary>
/// <remarks>
/// The file is <c>game-profiles.json</c>:
/// <code>
/// { "profiles": [ {
///     "name": "My game", "enabled": true,
///     "process": "Game.exe",              // or ["Game.exe", "GameLauncher.exe"]
///     "titleContains": "Game",             // optional
///     "mode": "UltraLow",                  // Auto | UltraLow | Balanced | Quality
///     "staticScanMaxDelayMs": 600,         // 0 = no slow-down on a still screen
///     "pauseWhenNotForeground": true,
///     "lowerPriority": true,
///     "glossary": "my-game.txt"
/// } ] }
/// </code>
/// Comments and trailing commas are allowed. A profile that cannot be understood is skipped and
/// reported; the rest still work. The first enabled profile that matches wins.
/// </remarks>
internal static class GameProfiles
{
    public const int MaxProfiles = 100;

    /// <summary>The result of reading the file: what was usable, and what was not.</summary>
    public sealed record ParseResult(IReadOnlyList<GameProfile> Profiles, IReadOnlyList<string> Problems);

    public static readonly ParseResult None = new([], []);

    public static ParseResult Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return None;

        var problems = new List<string>();
        var profiles = new List<GameProfile>();

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException ex)
        {
            return new ParseResult([], [$"JSON không hợp lệ: {ex.Message}"]);
        }

        using (doc)
        {
            var root = doc.RootElement;
            var list = root.ValueKind == JsonValueKind.Array ? root
                : root.ValueKind == JsonValueKind.Object && root.TryGetProperty("profiles", out var p) ? p
                : default;

            if (list.ValueKind != JsonValueKind.Array)
                return new ParseResult([], ["Thiếu mảng \"profiles\"."]);

            var index = 0;
            foreach (var item in list.EnumerateArray())
            {
                index++;
                if (profiles.Count >= MaxProfiles)
                {
                    problems.Add($"Chỉ dùng {MaxProfiles} profile đầu tiên.");
                    break;
                }

                var profile = ParseOne(item, index, problems);
                if (profile is not null) profiles.Add(profile);
            }
        }

        return new ParseResult(profiles, problems);
    }

    private static GameProfile? ParseOne(JsonElement item, int index, List<string> problems)
    {
        if (item.ValueKind != JsonValueKind.Object)
        {
            problems.Add($"Profile #{index}: không phải một đối tượng {{ }}.");
            return null;
        }

        if (GetBool(item, "enabled") == false) return null;

        var processes = new List<string>();
        if (item.TryGetProperty("process", out var proc))
        {
            if (proc.ValueKind == JsonValueKind.String) AddProcess(processes, proc.GetString());
            else if (proc.ValueKind == JsonValueKind.Array)
                foreach (var one in proc.EnumerateArray())
                    if (one.ValueKind == JsonValueKind.String) AddProcess(processes, one.GetString());
        }

        var title = GetString(item, "titleContains");
        if (string.IsNullOrWhiteSpace(title)) title = null;

        var name = GetString(item, "name");
        if (string.IsNullOrWhiteSpace(name)) name = processes.FirstOrDefault() ?? $"Profile #{index}";

        // Nothing to match on would apply to every window, which is never what was meant.
        if (processes.Count == 0 && title is null)
        {
            problems.Add($"Profile \"{name}\": cần \"process\" hoặc \"titleContains\" để biết áp dụng cho game nào.");
            return null;
        }

        PerformanceMode? mode = null;
        if (GetString(item, "mode") is { Length: > 0 } modeText)
        {
            if (Enum.TryParse<PerformanceMode>(modeText, ignoreCase: true, out var parsed)
                && Enum.IsDefined(parsed))
                mode = parsed;
            else
                problems.Add($"Profile \"{name}\": mode \"{modeText}\" không đúng (Auto, UltraLow, Balanced, Quality); bỏ qua mục này.");
        }

        int? delay = null;
        if (item.TryGetProperty("staticScanMaxDelayMs", out var d) && d.ValueKind == JsonValueKind.Number && d.TryGetInt32(out var ms))
            delay = Math.Clamp(ms, 0, PerformanceProfile.MaxStaticDelayMs);

        string? glossary = null;
        if (GetString(item, "glossary") is { Length: > 0 } g)
        {
            glossary = SafeFileName(g);
            if (glossary is null)
                problems.Add($"Profile \"{name}\": glossary phải là tên file (không có thư mục); bỏ qua mục này.");
        }

        return new GameProfile(
            name.Trim(),
            processes,
            title?.Trim(),
            mode,
            delay,
            GetBool(item, "pauseWhenNotForeground"),
            GetBool(item, "lowerPriority"),
            glossary);
    }

    /// <summary>The first profile that fits this window, or null.</summary>
    public static GameProfile? Match(IReadOnlyList<GameProfile> profiles, string? processName, string? windowTitle)
    {
        var process = NormaliseProcess(processName);

        foreach (var profile in profiles)
        {
            if (profile.Processes.Count > 0)
            {
                if (process.Length == 0) continue;
                if (!profile.Processes.Any(p => p.Equals(process, StringComparison.OrdinalIgnoreCase))) continue;
            }

            if (profile.TitleContains is { } needle
                && (windowTitle is null || windowTitle.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0))
                continue;

            return profile;
        }

        return null;
    }

    /// <summary>The settings with this game's choices laid over them; the input is not modified.</summary>
    public static PerformanceSettings Apply(PerformanceSettings settings, GameProfile? profile)
    {
        if (profile is null) return settings;

        return new PerformanceSettings
        {
            Mode = profile.Mode ?? settings.Mode,
            PauseWhenSourceInactive = settings.PauseWhenSourceInactive,
            PauseWhenSourceNotForeground = profile.PauseWhenNotForeground ?? settings.PauseWhenSourceNotForeground,
            StaticScanMaxDelayMs = profile.StaticScanMaxDelayMs ?? settings.StaticScanMaxDelayMs,
            UseTranslationCache = settings.UseTranslationCache,
            UseGameGlossary = settings.UseGameGlossary,
            LowerProcessPriority = profile.LowerPriority ?? settings.LowerProcessPriority,
        };
    }

    /// <summary>Executable name without folder, extension or case: <c>C:\x\Game.EXE</c> → <c>game</c>.</summary>
    public static string NormaliseProcess(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var file = name.Trim().Replace('/', '\\');
        var slash = file.LastIndexOf('\\');
        if (slash >= 0) file = file[(slash + 1)..];
        if (file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) file = file[..^4];
        return file.ToLowerInvariant();
    }

    /// <summary>The name if it is a plain file name, otherwise null.</summary>
    public static string? SafeFileName(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || trimmed is "." or "..") return null;
        // Both separators on every OS: a profile file written on one machine is read on another.
        if (trimmed.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || trimmed.IndexOfAny(['/', '\\']) >= 0) return null;
        return trimmed;
    }

    private static void AddProcess(List<string> list, string? raw)
    {
        var normal = NormaliseProcess(raw);
        if (normal.Length > 0 && !list.Contains(normal)) list.Add(normal);
    }

    private static string? GetString(JsonElement item, string key) =>
        item.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool? GetBool(JsonElement item, string key) =>
        item.TryGetProperty(key, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean()
            : null;
}
