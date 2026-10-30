using System.Runtime.InteropServices;

namespace EkipVisual.Services;

// Vraie saturation via ADL (AMD Display Library), le SDK officiel et documenté d'AMD —
// contrairement à NVIDIA où l'équivalent n'est accessible que via une interface privée non
// documentée. Structures/constantes vérifiées contre le SDK officiel public
// (GPUOpen-LibrariesAndSDKs/display-library : adl_defines.h, adl_structures.h, Sample/main.c).
// Sans carte AMD ou sans atiadlxx.dll : IsAvailable=false, l'app retombe sur la gamma ramp seule.
public sealed class AmdVibranceService : IDisposable
{
    private const int AdlMaxPath = 256;
    private const int AdlOk = 0;
    private const int ColorSaturation = 1 << 2;
    private const int ColorHue = 1 << 3;
    private const int DisplayConnected = 0x1;
    private const int DisplayMapped = 0x2;

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr MallocCallback(int size);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct AdapterInfo
    {
        public int iSize;
        public int iAdapterIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strUDID;
        public int iBusNumber;
        public int iDeviceNumber;
        public int iFunctionNumber;
        public int iVendorID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strAdapterName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strDisplayName;
        public int iPresent;
        public int iExist;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strDriverPath;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strDriverPathExt;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strPNPString;
        public int iOSDisplayIndex;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DisplayId
    {
        public int iDisplayLogicalIndex;
        public int iDisplayPhysicalIndex;
        public int iDisplayLogicalAdapterIndex;
        public int iDisplayPhysicalAdapterIndex;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    private struct DisplayInfo
    {
        public DisplayId displayID;
        public int iDisplayControllerIndex;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = AdlMaxPath)] public string strDisplayManufacturerName;
        public int iDisplayType;
        public int iDisplayOutputType;
        public int iDisplayConnector;
        public int iDisplayInfoMask;
        public int iDisplayInfoValue;
    }

    private readonly record struct Target(int AdapterIndex, int DisplayIndex);

    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Create(MallocCallback callback, int enumConnectedAdapters);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Main_Control_Destroy();
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_NumberOfAdapters_Get(out int numAdapters);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Adapter_AdapterInfo_Get(IntPtr adapterInfoArray, int bufferSizeBytes);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Display_DisplayInfo_Get(int adapterIndex, out int numDisplays, out IntPtr displayInfoArray, int forceDetect);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Display_Color_Get(int adapterIndex, int displayIndex, int colorType, out int current, out int def, out int min, out int max, out int step);
    [DllImport("atiadlxx.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ADL_Display_Color_Set(int adapterIndex, int displayIndex, int colorType, int current);

    // Doit rester référencée tant qu'ADL peut encore rappeler ce callback (jusqu'à Dispose) —
    // sinon le garbage collector peut la libérer pendant qu'ADL détient encore le pointeur natif.
    private MallocCallback? _mallocCallback;
    private readonly List<Target> _targets = new();
    private bool _initialized;

    public bool IsAvailable { get; }
    public int MinLevel { get; private set; }
    public int MaxLevel { get; private set; }
    public int DefaultLevel { get; private set; }

    public bool HasHue { get; private set; }
    public int HueMin { get; private set; }
    public int HueMax { get; private set; }
    public int DefaultHueAngle { get; private set; }

    public AmdVibranceService()
    {
        try { IsAvailable = TryInitialize(); }
        catch { IsAvailable = false; }
    }

    private bool TryInitialize()
    {
        _mallocCallback = Marshal.AllocHGlobal;
        if (ADL_Main_Control_Create(_mallocCallback, 1) != AdlOk) return false;
        _initialized = true;

        if (ADL_Adapter_NumberOfAdapters_Get(out int numAdapters) != AdlOk || numAdapters <= 0) return false;

        int adapterStructSize = Marshal.SizeOf<AdapterInfo>();
        var adaptersBuffer = Marshal.AllocHGlobal(adapterStructSize * numAdapters);
        try
        {
            for (int i = 0; i < adapterStructSize * numAdapters; i++) Marshal.WriteByte(adaptersBuffer, i, 0);
            if (ADL_Adapter_AdapterInfo_Get(adaptersBuffer, adapterStructSize * numAdapters) != AdlOk) return false;

            for (int i = 0; i < numAdapters; i++)
            {
                var info = Marshal.PtrToStructure<AdapterInfo>(adaptersBuffer + i * adapterStructSize);
                if (ADL_Display_DisplayInfo_Get(info.iAdapterIndex, out int numDisplays, out IntPtr displayInfoPtr, 0) != AdlOk) continue;
                try
                {
                    int displayStructSize = Marshal.SizeOf<DisplayInfo>();
                    for (int j = 0; j < numDisplays; j++)
                    {
                        var d = Marshal.PtrToStructure<DisplayInfo>(displayInfoPtr + j * displayStructSize);
                        bool connectedAndMapped = (d.iDisplayInfoValue & (DisplayConnected | DisplayMapped)) == (DisplayConnected | DisplayMapped);
                        if (!connectedAndMapped) continue;
                        if (info.iAdapterIndex != d.displayID.iDisplayLogicalAdapterIndex) continue;
                        _targets.Add(new Target(info.iAdapterIndex, d.displayID.iDisplayLogicalIndex));
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(displayInfoPtr);
                }
            }
        }
        finally
        {
            Marshal.FreeHGlobal(adaptersBuffer);
        }

        if (_targets.Count == 0) return false;

        var first = _targets[0];
        if (ADL_Display_Color_Get(first.AdapterIndex, first.DisplayIndex, ColorSaturation, out _, out int def, out int min, out int max, out _) != AdlOk)
            return false;

        MinLevel = min;
        MaxLevel = max;
        DefaultLevel = def;

        // La teinte est un ColorType ADL distinct et pas forcément supporté par tous les
        // écrans/pilotes — son absence ne doit pas faire échouer la vibrance elle-même.
        if (ADL_Display_Color_Get(first.AdapterIndex, first.DisplayIndex, ColorHue, out _, out int hueDef, out int hueMin, out int hueMax, out _) == AdlOk)
        {
            HasHue = true;
            HueMin = hueMin;
            HueMax = hueMax;
            DefaultHueAngle = hueDef;
        }

        return true;
    }

    public bool SetLevel(int level)
    {
        if (!IsAvailable) return false;
        int clamped = Math.Clamp(level, MinLevel, MaxLevel);
        bool allOk = true;
        foreach (var t in _targets)
            allOk &= ADL_Display_Color_Set(t.AdapterIndex, t.DisplayIndex, ColorSaturation, clamped) == AdlOk;
        return allOk;
    }

    public void RestoreDefault()
    {
        if (IsAvailable) SetLevel(DefaultLevel);
    }

    public bool SetHue(int angle)
    {
        if (!HasHue) return false;
        int clamped = Math.Clamp(angle, HueMin, HueMax);
        bool allOk = true;
        foreach (var t in _targets)
            allOk &= ADL_Display_Color_Set(t.AdapterIndex, t.DisplayIndex, ColorHue, clamped) == AdlOk;
        return allOk;
    }

    public void RestoreHueDefault()
    {
        if (HasHue) SetHue(DefaultHueAngle);
    }

    public void Dispose()
    {
        if (_initialized) ADL_Main_Control_Destroy();
    }
}
