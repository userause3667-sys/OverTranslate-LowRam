using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using OverTranslate.Services;
using OverTranslate.Services.Performance;

// The project also uses WinForms (tray icon), so these names are ambiguous without an alias.
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;

namespace OverTranslate.Views.Shell;

/// <summary>
/// A small always-on-top panel with the application's own memory, CPU and recognition numbers.
/// </summary>
/// <remarks>
/// Built in code, with no XAML and no resources of its own, and it samples once a second only while it
/// is open: closing it stops the timer and drops the window, so a closed panel costs nothing at all.
/// What it reads — the process counters and <see cref="PerfCounters"/> — exists whether or not it is
/// shown.
/// </remarks>
internal sealed class PerformanceMonitorWindow : Window
{
    private static PerformanceMonitorWindow? _instance;

    private readonly TextBlock _text = new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12,
        Foreground = new SolidColorBrush(Color.FromRgb(0xE6, 0xE6, 0xE6)),
        Margin = new Thickness(12, 10, 12, 6),
    };

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Process _process = Process.GetCurrentProcess();
    private TimeSpan _lastCpu;
    private long _lastTicks;
    private long _lastOcrRuns;

    /// <summary>Shows the panel, or closes it if it is already open.</summary>
    public static void Toggle()
    {
        if (_instance is not null)
        {
            _instance.Close();
            return;
        }

        _instance = new PerformanceMonitorWindow();
        _instance.Show();
    }

    private PerformanceMonitorWindow()
    {
        Title = "OverTranslate — Hiệu năng";
        Width = 360;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        Topmost = true;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = SystemParameters.WorkArea.Right - Width - 16;
        Top = SystemParameters.WorkArea.Bottom - 340;
        Background = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x24));

        var clearCache = new Button { Content = "Xóa cache dịch", Margin = new Thickness(0, 0, 8, 0), Padding = new Thickness(10, 3, 10, 3) };
        clearCache.Click += (_, _) =>
        {
            TranslationAssist.ClearCache();
            Refresh();
        };

        var openGlossary = new Button { Content = "Mở từ điển game", Padding = new Thickness(10, 3, 10, 3) };
        openGlossary.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(TranslationAssist.EnsureGlossaryFile()) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No program associated with .txt: nothing sensible to do from a panel.
            }
        };

        var openProfiles = new Button { Content = "Mở Game Profiles", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(10, 3, 10, 3) };
        openProfiles.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(GameProfileStore.EnsureFile()) { UseShellExecute = true });
            }
            catch (Exception)
            {
                // No program associated with .json: nothing sensible to do from a panel.
            }
        };

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(12, 4, 12, 12),
            Children = { clearCache, openGlossary, openProfiles },
        };

        Content = new StackPanel { Children = { _text, buttons } };

        _lastCpu = _process.TotalProcessorTime;
        _lastTicks = Stopwatch.GetTimestamp();
        _lastOcrRuns = PerfCounters.OcrRuns;

        _timer.Tick += (_, _) => Refresh();
        Loaded += (_, _) =>
        {
            Refresh();
            _timer.Start();
        };
        Closed += (_, _) =>
        {
            _timer.Stop();
            _process.Dispose();
            _instance = null;
        };
    }

    private void Refresh()
    {
        try
        {
            _process.Refresh();

            var now = Stopwatch.GetTimestamp();
            var cpu = _process.TotalProcessorTime;
            var elapsed = Stopwatch.GetElapsedTime(_lastTicks, now).TotalSeconds;
            var cpuPercent = elapsed > 0
                ? (cpu - _lastCpu).TotalSeconds / (elapsed * Environment.ProcessorCount) * 100
                : 0;
            var ocrRuns = PerfCounters.OcrRuns;
            var ocrPerSecond = elapsed > 0 ? (ocrRuns - _lastOcrRuns) / elapsed : 0;
            _lastCpu = cpu;
            _lastTicks = now;
            _lastOcrRuns = ocrRuns;

            var profile = PerformanceManager.Current;
            var settings = SettingsService.Instance.Current.Performance;
            var game = PerformanceManager.ActiveGameProfile;
            var problems = GameProfileStore.Problems;
            const double Mb = 1024.0 * 1024.0;

            _text.Text =
                $"Chế độ            : {profile.Level} (cài đặt: {settings.Mode})\n" +
                $"Game profile      : {(game is null ? "(không)" : game.Name)}{(problems.Count > 0 ? $"  ⚠ {problems.Count} lỗi trong game-profiles.json" : "")}\n" +
                $"Ưu tiên tiến trình: {_process.PriorityClass}\n" +
                $"RAM máy           : {SystemMemory.TotalPhysicalBytes / Mb:N0} MB (trống {SystemMemory.AvailablePhysicalBytes / Mb:N0} MB)\n" +
                $"Working Set       : {_process.WorkingSet64 / Mb:N1} MB\n" +
                $"Private Bytes     : {_process.PrivateMemorySize64 / Mb:N1} MB\n" +
                $"Managed Heap      : {GC.GetTotalMemory(false) / Mb:N1} MB\n" +
                $"CPU ứng dụng      : {cpuPercent:N1} %\n" +
                $"OCR gần nhất      : {PerfCounters.LastOcrMs} ms\n" +
                $"Lần nhận diện/giây: {ocrPerSecond:N1}\n" +
                $"Yêu cầu dịch chờ  : {PerfCounters.PendingTranslations}\n" +
                $"Dịch gần nhất     : {PerfCounters.LastTranslationMs} ms\n" +
                $"Cache dịch        : {TranslationAssist.CachedLines} dòng (trúng {PerfCounters.CacheHits}, trượt {PerfCounters.CacheMisses})";
        }
        catch (Exception ex)
        {
            _text.Text = "Không đọc được số liệu: " + ex.Message;
        }
    }
}
