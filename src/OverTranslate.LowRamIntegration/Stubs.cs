// Minimal stand-ins for the WPF/app types TranslationAssist touches, so the REAL TranslationAssist.cs
// (and manager, profile, cache, glossary, counters) can run on Linux. Member names/types mirror the
// originals in OcrService.cs / TranslationService.cs.
namespace System.Windows { public readonly record struct Rect(double X, double Y, double Width, double Height); }
namespace NLog
{
    public sealed class Logger { public void Info(string m, params object?[] a){} public void Debug(string m, params object?[] a){} public void Debug(Exception e, string m, params object?[] a){} public void Warn(Exception e, string m, params object?[] a){} public void Warn(string m, params object?[] a){} }
    public static class LogManager { public static Logger GetCurrentClassLogger() => new(); }
}
namespace OverTranslate.Services.Ocr { internal sealed class TiltedText {} }
namespace OverTranslate.Models
{
    public enum TranslationProvider { Google, Google2, GoogleChrome, Bing, Microsoft, DeepL, OpenAI, Youdao, TranSmart, Yandex }
    public static class LanguageData { public const string AutomaticSourceLanguage = "AUTO"; }
    public class AppSettings { public PerformanceSettings Performance { get; set; } = new(); }
}
namespace OverTranslate.Services
{
    using OverTranslate.Models;
    internal static class AppDataPaths { internal static string Root { get; } = Path.Combine(Path.GetTempPath(), "lowram-integ-" + Guid.NewGuid().ToString("N")); }
    public sealed class SettingsService { public static SettingsService Instance { get; } = new(); public AppSettings Current { get; } = new(); }
    public record OcrTextBlock(string Text, System.Windows.Rect Bounds, IReadOnlyList<System.Windows.Rect>? SourceLineBounds = null, double? RenderGlyphHeight = null)
    { public double? FontGlyphHeight { get; init; } public bool RunsAcross { get; init; } internal Ocr.TiltedText? Tilt { get; init; } }
    public record TranslatedBlock(string OriginalText, string TranslatedText, System.Windows.Rect Bounds, IReadOnlyList<System.Windows.Rect>? SourceLineBounds = null, double? RenderGlyphHeight = null)
    { public bool RunsAcross { get; init; } public double? FontGlyphHeight { get; init; } internal Ocr.TiltedText? Tilt { get; init; } public bool Untranslated { get; init; } }
}
namespace OverTranslate.Services.Providers { public sealed record TranslationContext(string Text); }
