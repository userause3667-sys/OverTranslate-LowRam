using System.Text.Json.Serialization;

namespace OverTranslate.Models;

/// <summary>Which resource profile the application runs with — Low-RAM Edition.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PerformanceMode
{
    /// <summary>Chosen from the installed RAM: under 6 GB is Ultra Low, under 12 GB Balanced, else Quality.</summary>
    Auto,

    /// <summary>Designed for a 4 GB machine: releases memory early and scans a still screen less often.</summary>
    UltraLow,

    /// <summary>A middle road for 6–12 GB machines.</summary>
    Balanced,

    /// <summary>The behaviour of the original application: nothing is traded away for memory.</summary>
    Quality,
}

/// <summary>
/// Everything the Low-RAM Edition adds to the settings file, under one key (<c>Performance</c>).
/// </summary>
/// <remarks>
/// A group of its own, as <see cref="RealtimeSettings"/> is, so a settings file written by the original
/// application — which has no such key — reads back as these defaults and nothing the user had set is
/// touched. Every value here can be edited by hand in <c>appsettings.json</c>.
/// </remarks>
public class PerformanceSettings
{
    /// <summary>The resource profile. <see cref="PerformanceMode.Auto"/> picks one from the installed RAM.</summary>
    public PerformanceMode Mode { get; set; } = PerformanceMode.Auto;

    /// <summary>
    /// Stop reading the screen while the window being watched is minimised or gone, and let the
    /// recognition model go if that lasts. Only applies to realtime translation of a single window.
    /// </summary>
    public bool PauseWhenSourceInactive { get; set; } = true;

    /// <summary>
    /// Also treat a watched window that is not in front as inactive. Off by default: a game in a
    /// window beside the one being typed in is still being watched. Windows of this application count
    /// as being in front.
    /// </summary>
    public bool PauseWhenSourceNotForeground { get; set; } = false;

    /// <summary>
    /// The longest extra wait, in milliseconds, added between looks while a watched region is still,
    /// or null for the profile's own value. 0 switches the slow-down off. Capped at 2000.
    /// </summary>
    public int? StaticScanMaxDelayMs { get; set; }

    /// <summary>Keep translations between sessions and sort out repeated lines before asking an engine.</summary>
    public bool UseTranslationCache { get; set; } = true;

    /// <summary>Apply <c>glossary.txt</c> (game terms) to translations.</summary>
    public bool UseGameGlossary { get; set; } = true;

    /// <summary>
    /// Run this application at below-normal CPU priority, so a game that wants every core wins them
    /// and recognition only uses what is left. Null (the default) follows the profile: on for Ultra
    /// Low and Balanced, off for Quality. Only ever changes a process that is at normal priority.
    /// </summary>
    public bool? LowerProcessPriority { get; set; }
}
