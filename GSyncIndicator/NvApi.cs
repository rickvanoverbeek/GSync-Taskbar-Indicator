using System.Runtime.InteropServices;

namespace GSyncIndicator;

/// <summary>
/// Thin P/Invoke layer over NVIDIA's NVAPI (nvapi64.dll).
///
/// NVAPI does not export its functions by name. Instead the DLL exports a single
/// entry point, <c>nvapi_QueryInterface</c>, which returns a function pointer for a
/// given 32-bit function id. We resolve the handful of functions we need through it.
///
/// Function ids below come from NVIDIA's public header nvapi_interface.h
/// (https://github.com/NVIDIA/nvapi). They are stable across driver versions.
/// </summary>
internal static class NvApi
{
    // ---- Function ids (from nvapi_interface.h) --------------------------------
    private const uint ID_Initialize                 = 0x0150E828;
    private const uint ID_Unload                     = 0xD22BDD7E;
    private const uint ID_GetErrorMessage            = 0x6C2D048C;
    private const uint ID_EnumPhysicalGPUs           = 0xE5AC921F;
    private const uint ID_GPU_GetConnectedDisplayIds = 0x0078DBA2;
    private const uint ID_DISP_GetGDIPrimaryDisplayId = 0x1E9D8A31;
    private const uint ID_DISP_GetAdaptiveSyncData   = 0xB73D1EE9;
    private const uint ID_SYS_GetDriverAndBranchVersion = 0x2926AAAD;
    private const uint ID_GetInterfaceVersionString  = 0x01053FA5;

    private const int NVAPI_OK = 0;
    private const int NVAPI_MAX_PHYSICAL_GPUS = 64;
    private const int NVAPI_SHORT_STRING_MAX = 64;

    // ---- The single real export ----------------------------------------------
    [DllImport("nvapi64.dll", EntryPoint = "nvapi_QueryInterface", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr nvapi_QueryInterface(uint id);

    // ---- Delegates for the resolved function pointers -------------------------
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Initialize_t();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int Unload_t();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetErrorMessage_t(int status, [Out] byte[] szDesc);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int EnumPhysicalGPUs_t([Out] IntPtr[] handles, out int count);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetConnectedDisplayIds_t(IntPtr gpu, [In, Out] NV_GPU_DISPLAYIDS[]? ids, ref uint count, uint flags);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetGDIPrimaryDisplayId_t(out uint displayId);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetAdaptiveSyncData_t(uint displayId, ref NV_GET_ADAPTIVE_SYNC_DATA_V1 data);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetDriverAndBranchVersion_t(out uint version, [Out] byte[] branch);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetInterfaceVersionString_t([Out] byte[] desc);

    private static Initialize_t?             _initialize;
    private static Unload_t?                 _unload;
    private static GetErrorMessage_t?        _getErrorMessage;
    private static EnumPhysicalGPUs_t?       _enumGpus;
    private static GetConnectedDisplayIds_t? _getConnectedDisplayIds;
    private static GetGDIPrimaryDisplayId_t? _getGdiPrimary;
    private static GetAdaptiveSyncData_t?    _getAdaptiveSync;
    private static GetDriverAndBranchVersion_t? _getDriverVersion;
    private static GetInterfaceVersionString_t? _getInterfaceVersion;

    /// <summary>True once <see cref="Initialize"/> has succeeded.</summary>
    public static bool Available { get; private set; }

    /// <summary>Human-readable reason NVAPI could not be used, when <see cref="Available"/> is false.</summary>
    public static string? UnavailableReason { get; private set; }

    // ---- Native structures ----------------------------------------------------

    /// <summary>Mirrors NV_GPU_DISPLAYIDS. We only read <c>displayId</c>; the rest is opaque.</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    private struct NV_GPU_DISPLAYIDS
    {
        public uint version;
        public uint connectorType;
        public uint displayId;
        public uint flags;
    }

    /// <summary>Mirrors NV_GET_ADAPTIVE_SYNC_DATA_V1 (40 bytes).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 8)]
    public struct NV_GET_ADAPTIVE_SYNC_DATA_V1
    {
        public uint version;
        public uint maxFrameInterval;   // microseconds; 0 when EDID defaults are used
        public uint flags;              // bit0 = bDisableAdaptiveSync, bit1 = bDisableFrameSplitting
        public uint lastFlipRefreshCount;
        public ulong lastFlipTimeStamp;
        public uint reserved1_0;
        public uint reserved1_1;
        public uint reserved1_2;
        public uint reserved1_3;

        public readonly bool AdaptiveSyncDisabled => (flags & 0x1) != 0;
    }

    /// <summary>Snapshot of one display's adaptive-sync state.</summary>
    public readonly record struct AdaptiveSyncInfo(
        bool Supported,
        bool Disabled,
        uint MaxFrameIntervalMicros,
        uint LastFlipRefreshCount,
        ulong LastFlipTimeStamp);

    // NVAPI's MAKE_NVAPI_VERSION(t, ver) == sizeof(t) | (ver << 16)
    private static uint MakeVersion<T>(uint ver) where T : struct
        => (uint)Marshal.SizeOf<T>() | (ver << 16);

    private static T? Resolve<T>(uint id) where T : Delegate
    {
        IntPtr p = nvapi_QueryInterface(id);
        return p == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer<T>(p);
    }

    /// <summary>
    /// Resolves the required NVAPI functions and calls NvAPI_Initialize.
    /// Returns false (with <see cref="UnavailableReason"/> set) on any non-NVIDIA system.
    /// </summary>
    public static bool Initialize()
    {
        if (Available) return true;
        try
        {
            _initialize             = Resolve<Initialize_t>(ID_Initialize);
            _unload                 = Resolve<Unload_t>(ID_Unload);
            _getErrorMessage        = Resolve<GetErrorMessage_t>(ID_GetErrorMessage);
            _enumGpus               = Resolve<EnumPhysicalGPUs_t>(ID_EnumPhysicalGPUs);
            _getConnectedDisplayIds = Resolve<GetConnectedDisplayIds_t>(ID_GPU_GetConnectedDisplayIds);
            _getGdiPrimary          = Resolve<GetGDIPrimaryDisplayId_t>(ID_DISP_GetGDIPrimaryDisplayId);
            _getAdaptiveSync        = Resolve<GetAdaptiveSyncData_t>(ID_DISP_GetAdaptiveSyncData);
            _getDriverVersion       = Resolve<GetDriverAndBranchVersion_t>(ID_SYS_GetDriverAndBranchVersion);
            _getInterfaceVersion    = Resolve<GetInterfaceVersionString_t>(ID_GetInterfaceVersionString);

            if (_initialize is null)
            {
                UnavailableReason = "NVAPI is present but NvAPI_Initialize could not be resolved.";
                return false;
            }
            if (_getAdaptiveSync is null)
            {
                UnavailableReason = "This NVIDIA driver does not expose the adaptive-sync (G-Sync) query.";
                return false;
            }

            int status = _initialize();
            if (status != NVAPI_OK)
            {
                UnavailableReason = $"NvAPI_Initialize failed: {DescribeStatus(status)}";
                return false;
            }

            Available = true;
            UnavailableReason = null;
            return true;
        }
        catch (DllNotFoundException)
        {
            UnavailableReason = "nvapi64.dll not found. An NVIDIA GPU and driver are required.";
            return false;
        }
        catch (Exception ex)
        {
            UnavailableReason = "Failed to initialize NVAPI: " + ex.Message;
            return false;
        }
    }

    public static void Unload()
    {
        try { if (Available) _unload?.Invoke(); }
        catch { /* best effort on shutdown */ }
        Available = false;
    }

    /// <summary>
    /// Returns the display ids of all currently connected displays across all NVIDIA GPUs,
    /// with the GDI primary display id included. Empty when NVAPI is unavailable.
    /// </summary>
    public static IReadOnlyList<uint> GetDisplayIds()
    {
        var ids = new List<uint>();
        if (!Available) return ids;

        try
        {
            if (_enumGpus is not null && _getConnectedDisplayIds is not null)
            {
                var gpus = new IntPtr[NVAPI_MAX_PHYSICAL_GPUS];
                if (_enumGpus(gpus, out int gpuCount) == NVAPI_OK)
                {
                    for (int i = 0; i < gpuCount; i++)
                    {
                        foreach (uint id in GetConnectedDisplayIdsForGpu(gpus[i]))
                            if (!ids.Contains(id)) ids.Add(id);
                    }
                }
            }
        }
        catch { /* fall through to the primary-display fallback */ }

        // Always make sure the primary display is covered, even if enumeration failed.
        try
        {
            if (_getGdiPrimary is not null &&
                _getGdiPrimary(out uint primary) == NVAPI_OK &&
                primary != 0 && !ids.Contains(primary))
            {
                ids.Add(primary);
            }
        }
        catch { /* ignore */ }

        return ids;
    }

    private static IEnumerable<uint> GetConnectedDisplayIdsForGpu(IntPtr gpu)
    {
        if (_getConnectedDisplayIds is null || gpu == IntPtr.Zero)
            yield break;

        // First pass: pass a null array to learn how many displays are connected.
        uint count = 0;
        if (_getConnectedDisplayIds(gpu, null, ref count, 0) != NVAPI_OK || count == 0)
            yield break;

        // Second pass: fill the array. Every element must carry the struct version.
        var arr = new NV_GPU_DISPLAYIDS[count];
        uint ver = MakeVersion<NV_GPU_DISPLAYIDS>(3);
        for (int i = 0; i < arr.Length; i++) arr[i].version = ver;

        if (_getConnectedDisplayIds(gpu, arr, ref count, 0) != NVAPI_OK)
            yield break;

        for (int i = 0; i < count && i < arr.Length; i++)
            if (arr[i].displayId != 0)
                yield return arr[i].displayId;
    }

    /// <summary>The GDI primary display id, or 0 if unavailable.</summary>
    public static uint GetPrimaryDisplayId()
    {
        try
        {
            if (Available && _getGdiPrimary is not null &&
                _getGdiPrimary(out uint id) == NVAPI_OK)
                return id;
        }
        catch { /* ignore */ }
        return 0;
    }

    /// <summary>
    /// Reads adaptive-sync (G-Sync) data for one display.
    /// <c>Supported == false</c> means the display does not report adaptive-sync data
    /// (e.g. a non-G-Sync monitor) or the query failed for it.
    /// </summary>
    public static AdaptiveSyncInfo GetAdaptiveSync(uint displayId)
    {
        if (!Available || _getAdaptiveSync is null)
            return new AdaptiveSyncInfo(false, false, 0, 0, 0);

        var data = new NV_GET_ADAPTIVE_SYNC_DATA_V1
        {
            version = MakeVersion<NV_GET_ADAPTIVE_SYNC_DATA_V1>(1)
        };

        try
        {
            int status = _getAdaptiveSync(displayId, ref data);
            if (status != NVAPI_OK)
                return new AdaptiveSyncInfo(false, false, 0, 0, 0);

            return new AdaptiveSyncInfo(
                Supported: true,
                Disabled: data.AdaptiveSyncDisabled,
                MaxFrameIntervalMicros: data.maxFrameInterval,
                LastFlipRefreshCount: data.lastFlipRefreshCount,
                LastFlipTimeStamp: data.lastFlipTimeStamp);
        }
        catch
        {
            return new AdaptiveSyncInfo(false, false, 0, 0, 0);
        }
    }

    /// <summary>Raw adaptive-sync read that also surfaces the NVAPI status code (for diagnostics).</summary>
    public readonly record struct AdaptiveSyncRaw(
        int Status, bool Disabled, bool FrameSplittingDisabled,
        uint MaxFrameIntervalMicros, uint LastFlipRefreshCount, ulong LastFlipTimeStamp);

    public static AdaptiveSyncRaw GetAdaptiveSyncRaw(uint displayId)
    {
        if (!Available || _getAdaptiveSync is null)
            return new AdaptiveSyncRaw(int.MinValue, false, false, 0, 0, 0);

        var data = new NV_GET_ADAPTIVE_SYNC_DATA_V1
        {
            version = MakeVersion<NV_GET_ADAPTIVE_SYNC_DATA_V1>(1)
        };
        try
        {
            int status = _getAdaptiveSync(displayId, ref data);
            return new AdaptiveSyncRaw(
                status,
                (data.flags & 0x1) != 0,
                (data.flags & 0x2) != 0,
                data.maxFrameInterval,
                data.lastFlipRefreshCount,
                data.lastFlipTimeStamp);
        }
        catch (Exception ex)
        {
            return new AdaptiveSyncRaw(int.MinValue, false, false, 0, 0, (ulong)ex.HResult);
        }
    }

    /// <summary>Human-readable NVAPI status text (via NvAPI_GetErrorMessage when available).</summary>
    public static string DescribeStatusText(int status) => DescribeStatus(status);

    /// <summary>Driver version string like "551.86 (branch r550_00)", or null if unavailable.</summary>
    public static string? GetDriverVersion()
    {
        try
        {
            if (Available && _getDriverVersion is not null)
            {
                var branch = new byte[NVAPI_SHORT_STRING_MAX];
                if (_getDriverVersion(out uint ver, branch) == NVAPI_OK)
                {
                    string b = System.Text.Encoding.ASCII.GetString(branch).TrimEnd('\0', ' ');
                    return $"{ver / 100}.{ver % 100:D2} (branch {b})";
                }
            }
        }
        catch { /* ignore */ }
        return null;
    }

    private static string? GetInterfaceVersion()
    {
        try
        {
            if (_getInterfaceVersion is not null)
            {
                var buf = new byte[NVAPI_SHORT_STRING_MAX];
                if (_getInterfaceVersion(buf) == NVAPI_OK)
                    return System.Text.Encoding.ASCII.GetString(buf).TrimEnd('\0', ' ');
            }
        }
        catch { /* ignore */ }
        return null;
    }

    /// <summary>
    /// Builds a full diagnostic report of what NVAPI reports for every display, including
    /// raw status codes and flags. Meant to be copied and shared for troubleshooting.
    /// </summary>
    public static string BuildReport()
    {
        var sb = new System.Text.StringBuilder();
        Initialize();

        sb.AppendLine($"NVAPI available : {Available}");
        if (!Available)
        {
            sb.AppendLine($"Reason          : {UnavailableReason}");
            return sb.ToString();
        }

        sb.AppendLine($"Driver version  : {GetDriverVersion() ?? "(unknown)"}");
        sb.AppendLine($"NVAPI interface : {GetInterfaceVersion() ?? "(unknown)"}");
        sb.AppendLine($"AdaptiveSync ver: 0x{MakeVersion<NV_GET_ADAPTIVE_SYNC_DATA_V1>(1):X} " +
                      $"(size {Marshal.SizeOf<NV_GET_ADAPTIVE_SYNC_DATA_V1>()})");

        uint primary = GetPrimaryDisplayId();
        var ids = GetDisplayIds();
        sb.AppendLine($"Primary display : 0x{primary:X8}");
        sb.AppendLine($"Displays found  : {ids.Count}");
        sb.AppendLine();

        if (ids.Count == 0)
        {
            sb.AppendLine("No display ids were returned by NVAPI.");
            return sb.ToString();
        }

        foreach (uint id in ids)
        {
            string tag = id == primary && primary != 0 ? " (primary)" : "";
            var r = GetAdaptiveSyncRaw(id);
            sb.AppendLine($"Display 0x{id:X8}{tag}");
            sb.AppendLine($"  GetAdaptiveSyncData: status {r.Status} = {DescribeStatus(r.Status)}");
            if (r.Status == NVAPI_OK)
            {
                sb.AppendLine($"  bDisableAdaptiveSync   : {(r.Disabled ? 1 : 0)}");
                sb.AppendLine($"  bDisableFrameSplitting : {(r.FrameSplittingDisabled ? 1 : 0)}");
                sb.AppendLine($"  maxFrameInterval       : {r.MaxFrameIntervalMicros} us");
                sb.AppendLine($"  lastFlipRefreshCount   : {r.LastFlipRefreshCount}");
                sb.AppendLine($"  lastFlipTimeStamp      : {r.LastFlipTimeStamp}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string DescribeStatus(int status)
    {
        if (status == int.MinValue) return "call threw / unavailable";
        try
        {
            if (_getErrorMessage is not null)
            {
                var buf = new byte[NVAPI_SHORT_STRING_MAX];
                if (_getErrorMessage(status, buf) == NVAPI_OK)
                {
                    string s = System.Text.Encoding.ASCII.GetString(buf).TrimEnd('\0', ' ');
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                }
            }
        }
        catch { /* ignore */ }
        return $"NVAPI status {status}";
    }
}
