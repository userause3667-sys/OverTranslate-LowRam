namespace OverTranslate.Services.Realtime.Capture;

/// <summary>
/// Implemented by a capture backend that watches one window, so a session can tell that window is no
/// longer worth reading (minimised, closed, behind something else) without grabbing a frame to find out.
/// </summary>
/// <remarks>
/// A backend that reads the whole screen does not implement it: the screen is always there.
/// </remarks>
public interface ISourceActivity
{
    /// <summary>The window exists, is visible and is not minimised. A cheap call; any thread.</summary>
    bool IsSourceActive { get; }

    /// <summary>
    /// The window is the one in front — or something of this application's own is, since its overlays
    /// and control bar take the foreground while the user works with them. A cheap call; any thread.
    /// </summary>
    bool IsSourceForeground { get; }

    /// <summary>Executable name of the process that owns the window, or null when it cannot be told.</summary>
    string? SourceProcessName => null;

    /// <summary>Title of the window, or null when it cannot be told.</summary>
    string? SourceWindowTitle => null;
}
