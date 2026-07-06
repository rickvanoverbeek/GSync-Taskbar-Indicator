namespace GSyncIndicator;

/// <summary>Overall G-Sync state, most-specific first.</summary>
public enum GSyncState
{
    /// <summary>No NVIDIA GPU/driver, or no display reports adaptive-sync (G-Sync) support.</summary>
    Unavailable,
    /// <summary>A G-Sync-capable display is present, but G-Sync is turned off in the driver.</summary>
    Disabled,
    /// <summary>G-Sync is enabled and a capable display is present, but VRR is not driving it right now.</summary>
    Ready,
    /// <summary>G-Sync is actively driving the refresh rate right now (a game/app is presenting with VRR).</summary>
    Active
}

/// <summary>Per-display detail used for the tooltip and menu.</summary>
public readonly record struct DisplayStatus(
    uint DisplayId,
    bool IsPrimary,
    bool Capable,
    bool ActiveNow);

/// <summary>Aggregate result of a single poll.</summary>
public sealed class GSyncStatus
{
    public GSyncState State { get; init; }
    public IReadOnlyList<DisplayStatus> Displays { get; init; } = Array.Empty<DisplayStatus>();
    public string? Note { get; init; }

    public string ShortLabel => State switch
    {
        GSyncState.Active   => "G-Sync: ACTIVE",
        GSyncState.Ready    => "G-Sync: on (idle)",
        GSyncState.Disabled => "G-Sync: off",
        _                   => "G-Sync: unavailable"
    };
}

/// <summary>
/// Polls NVAPI and turns the raw adaptive-sync data into a single <see cref="GSyncStatus"/>.
///
/// Detection is based purely on the adaptive-sync flip counter advancing: it only moves when
/// variable refresh is actually driving a display (it stays frozen on a static desktop and
/// during fixed-refresh output). NVAPI's <c>bDisableAdaptiveSync</c> flag is deliberately
/// ignored — on real hardware it reads the same whether G-Sync is on or off, so it can't be
/// trusted.
///
///   * Active — the flip counter is advancing (VRR is driving the screen now)
///   * Ready  — the display is G-Sync-capable (the query succeeds) but not being driven
///   * (none) — the query fails, i.e. the display has no adaptive-sync support
///
/// Note: because the driver reports identical data whether G-Sync is merely *enabled* or fully
/// *disabled* while idle, "idle-but-enabled" and "disabled" both surface as Ready.
/// </summary>
public sealed class GSyncMonitor
{
    // A real game advances the flip counter by roughly its frame rate each second; stray
    // desktop repaints (if any) produce only a flip or two. Require a clear rate, or two
    // consecutive advancing polls, before calling it "active".
    private const uint ActiveFlipDelta = 5;
    private const int  ActiveStreak = 2;

    // The global G-Sync switch (VRR_MODE) rarely changes, and reading it spins up a DRS
    // session, so refresh it only every few polls rather than every second.
    private const int VrrRefreshEveryPolls = 5;

    private readonly Dictionary<uint, (uint count, int streak)> _flips = new();
    private bool _initTried;
    private int _pollCount;
    private int _vrrMode = -1;   // -1 unknown, 0 disabled, 1/2 enabled

    public GSyncStatus Poll()
    {
        if (!_initTried)
        {
            _initTried = true;
            NvApi.Initialize();
        }

        if (!NvApi.Available)
        {
            _flips.Clear();
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
            _flips.Clear();
            return new GSyncStatus
            {
                State = GSyncState.Unavailable,
                Note = "No NVIDIA displays were found."
            };
        }

        if (_pollCount % VrrRefreshEveryPolls == 0)
            _vrrMode = NvApi.GetVrrMode();
        _pollCount++;

        var displays = new List<DisplayStatus>(ids.Count);
        bool anyActive = false, anyCapable = false;

        foreach (uint id in ids)
        {
            var info = NvApi.GetAdaptiveSync(id);
            bool isPrimary = id == primary && primary != 0;

            if (!info.Supported)
            {
                _flips.Remove(id);
                displays.Add(new DisplayStatus(id, isPrimary, Capable: false, ActiveNow: false));
                continue;
            }

            anyCapable = true;
            bool activeNow = UpdateActivity(id, info.LastFlipRefreshCount);
            if (activeNow) anyActive = true;

            displays.Add(new DisplayStatus(id, isPrimary, Capable: true, ActiveNow: activeNow));
        }

        PruneMissing(ids);

        // _vrrMode == 0 means G-Sync is switched off in the driver; treat that as Disabled
        // even though a capable display is present. Unknown (-1) falls back to Ready.
        GSyncState state =
            anyActive              ? GSyncState.Active :
            !anyCapable            ? GSyncState.Unavailable :
            _vrrMode == 0          ? GSyncState.Disabled :
                                     GSyncState.Ready;

        string? note = state switch
        {
            GSyncState.Unavailable => "Displays were found, but none report G-Sync / adaptive-sync support.",
            GSyncState.Disabled    => "G-Sync is turned off in the NVIDIA Control Panel / NVIDIA App.",
            _                      => null
        };

        return new GSyncStatus { State = state, Displays = displays, Note = note };
    }

    /// <summary>Updates the per-display flip history and returns whether VRR is driving it now.</summary>
    private bool UpdateActivity(uint id, uint count)
    {
        int streak = 0;
        bool active = false;

        if (_flips.TryGetValue(id, out var prev))
        {
            // The counter is unsigned and could wrap; treat only forward movement as a flip.
            uint delta = count >= prev.count ? count - prev.count : 0;
            if (delta > 0)
            {
                streak = prev.streak + 1;
                active = delta >= ActiveFlipDelta || streak >= ActiveStreak;
            }
        }

        _flips[id] = (count, streak);
        return active;
    }

    private void PruneMissing(IReadOnlyList<uint> present)
    {
        if (_flips.Count == 0) return;
        var stale = _flips.Keys.Where(k => !present.Contains(k)).ToList();
        foreach (var k in stale) _flips.Remove(k);
    }
}
