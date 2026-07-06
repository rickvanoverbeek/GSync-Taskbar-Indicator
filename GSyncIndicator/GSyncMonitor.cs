namespace GSyncIndicator;

/// <summary>Overall G-Sync state, most-specific first.</summary>
public enum GSyncState
{
    /// <summary>No NVIDIA GPU/driver, or no display reports adaptive-sync (G-Sync) support.</summary>
    Unavailable,
    /// <summary>A G-Sync-capable display is present but variable refresh is not engaged right now.</summary>
    Ready,
    /// <summary>G-Sync is engaged and driving the refresh rate right now.</summary>
    Active
}

/// <summary>Per-display detail used for the tooltip and menu.</summary>
public readonly record struct DisplayStatus(
    uint DisplayId,
    bool IsPrimary,
    bool Capable,
    bool Engaged,
    bool ActiveNow);

/// <summary>Aggregate result of a single poll.</summary>
public sealed class GSyncStatus
{
    public GSyncState State { get; init; }
    public IReadOnlyList<DisplayStatus> Displays { get; init; } = Array.Empty<DisplayStatus>();
    public string? Note { get; init; }

    public string ShortLabel => State switch
    {
        GSyncState.Active => "G-Sync: ACTIVE",
        GSyncState.Ready  => "G-Sync: on (idle)",
        _                 => "G-Sync: unavailable"
    };
}

/// <summary>
/// Polls NVAPI and turns the raw adaptive-sync data into a single <see cref="GSyncStatus"/>.
///
/// NVAPI's <c>bDisableAdaptiveSync</c> flag reflects whether variable refresh is *engaged
/// right now*, not whether G-Sync is enabled in the driver: on a static desktop it reads
/// "disabled" even for a G-Sync monitor with G-Sync turned on. So a display is treated as:
///   * Active  — adaptive sync is engaged (flag clear), and flips are advancing
///   * Ready   — the display is G-Sync-capable (the query succeeds) but not engaged/presenting
///   * (absent)— the query fails, i.e. the display has no adaptive-sync support
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
        bool anyActive = false, anyCapable = false;

        foreach (uint id in ids)
        {
            var info = NvApi.GetAdaptiveSync(id);
            bool isPrimary = id == primary && primary != 0;

            if (!info.Supported)
            {
                // Query failed -> this display has no adaptive-sync support.
                _lastFlipTimestamps.Remove(id);
                displays.Add(new DisplayStatus(id, isPrimary, Capable: false, Engaged: false, ActiveNow: false));
                continue;
            }

            anyCapable = true;
            bool engaged = !info.Disabled;   // flag clear == adaptive sync engaged right now
            bool activeNow = false;

            if (engaged)
            {
                // Confirm frames are actually being presented (timestamp advancing).
                if (_lastFlipTimestamps.TryGetValue(id, out ulong prev))
                    activeNow = info.LastFlipTimeStamp > prev;
                _lastFlipTimestamps[id] = info.LastFlipTimeStamp;
                if (activeNow) anyActive = true;
            }
            else
            {
                _lastFlipTimestamps.Remove(id);
            }

            displays.Add(new DisplayStatus(id, isPrimary, Capable: true, Engaged: engaged, ActiveNow: activeNow));
        }

        PruneMissing(ids);

        GSyncState state =
            anyActive  ? GSyncState.Active :
            anyCapable ? GSyncState.Ready  :
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
