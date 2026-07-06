namespace GSyncIndicator;

/// <summary>Overall G-Sync state, most-specific first.</summary>
public enum GSyncState
{
    /// <summary>No NVIDIA GPU / driver, or no displays could be queried.</summary>
    Unavailable,
    /// <summary>G-Sync-capable display(s) found, but adaptive sync is turned off for all of them.</summary>
    Off,
    /// <summary>G-Sync is enabled and ready, but nothing is currently driving variable refresh.</summary>
    Ready,
    /// <summary>G-Sync is actively driving the refresh rate right now (a game/app is presenting).</summary>
    Active
}

/// <summary>Per-display detail used for the tooltip and menu.</summary>
public readonly record struct DisplayStatus(
    uint DisplayId,
    bool IsPrimary,
    bool Supported,
    bool Enabled,
    bool ActiveNow);

/// <summary>Aggregate result of a single poll.</summary>
public sealed class GSyncStatus
{
    public GSyncState State { get; init; }
    public IReadOnlyList<DisplayStatus> Displays { get; init; } = Array.Empty<DisplayStatus>();
    public string? Note { get; init; }

    public string ShortLabel => State switch
    {
        GSyncState.Active      => "G-Sync: ACTIVE",
        GSyncState.Ready       => "G-Sync: on (idle)",
        GSyncState.Off         => "G-Sync: off",
        _                      => "G-Sync: unavailable"
    };
}

/// <summary>
/// Polls NVAPI and turns the raw adaptive-sync data into a single <see cref="GSyncStatus"/>.
///
/// "Active" is inferred from the adaptive-sync flip timestamp advancing between polls:
/// when variable refresh is actually driving a display, its last-flip timestamp keeps
/// moving; on a static desktop it does not. This mirrors what NVIDIA's own on-screen
/// G-Sync indicator reflects.
/// </summary>
public sealed class GSyncMonitor
{
    private readonly Dictionary<uint, ulong> _lastFlipTimestamps = new();
    private bool _initTried;

    public GSyncStatus Poll()
    {
        if (!_initTried)
        {
            _initTried = true;
            NvApi.Initialize();
        }

        if (!NvApi.Available)
        {
            _lastFlipTimestamps.Clear();
            return new GSyncStatus
            {
                State = GSyncState.Unavailable,
                Note = NvApi.UnavailableReason ?? "NVAPI is unavailable."
            };
        }

        var ids = NvApi.GetDisplayIds();
        uint primary = NvApi.GetPrimaryDisplayId();

        if (ids.Count == 0)
        {
            _lastFlipTimestamps.Clear();
            return new GSyncStatus
            {
                State = GSyncState.Unavailable,
                Note = "No NVIDIA displays were found."
            };
        }

        var displays = new List<DisplayStatus>(ids.Count);
        bool anyActive = false, anyEnabled = false, anySupported = false;

        foreach (uint id in ids)
        {
            var info = NvApi.GetAdaptiveSync(id);
            bool isPrimary = id == primary && primary != 0;

            if (!info.Supported)
            {
                _lastFlipTimestamps.Remove(id);
                displays.Add(new DisplayStatus(id, isPrimary, Supported: false, Enabled: false, ActiveNow: false));
                continue;
            }

            anySupported = true;
            bool enabled = !info.Disabled;
            bool activeNow = false;

            if (enabled)
            {
                anyEnabled = true;
                if (_lastFlipTimestamps.TryGetValue(id, out ulong prev))
                    activeNow = info.LastFlipTimeStamp > prev;
                _lastFlipTimestamps[id] = info.LastFlipTimeStamp;
                if (activeNow) anyActive = true;
            }
            else
            {
                _lastFlipTimestamps.Remove(id);
            }

            displays.Add(new DisplayStatus(id, isPrimary, Supported: true, Enabled: enabled, ActiveNow: activeNow));
        }

        // Drop timestamps for displays that disappeared (e.g. unplugged).
        PruneMissing(ids);

        GSyncState state =
            anyActive    ? GSyncState.Active :
            anyEnabled   ? GSyncState.Ready  :
            anySupported ? GSyncState.Off    :
                           GSyncState.Unavailable;

        string? note = state == GSyncState.Unavailable
            ? "Displays were found, but none report G-Sync / adaptive-sync support."
            : null;

        return new GSyncStatus { State = state, Displays = displays, Note = note };
    }

    private void PruneMissing(IReadOnlyList<uint> present)
    {
        if (_lastFlipTimestamps.Count == 0) return;
        var stale = _lastFlipTimestamps.Keys.Where(k => !present.Contains(k)).ToList();
        foreach (var k in stale) _lastFlipTimestamps.Remove(k);
    }
}
