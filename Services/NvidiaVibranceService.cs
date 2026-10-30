using System.Runtime.InteropServices;

namespace EkipVisual.Services;

// Vraie saturation/vibrance (comme le slider "Digital Vibrance" du panneau NVIDIA) — impossible
// via gamma ramp (voir GammaRampService, limite mathématique). NVAPI n'expose pas ses fonctions
// par DllImport classique : un seul point d'entrée (nvapi_QueryInterface) retourne les vrais
// pointeurs de fonction à partir d'IDs numériques figés. Ces IDs et la structure ne sont pas
// documentés officiellement par NVIDIA, mais sont stables depuis de nombreuses années et utilisés
// par des outils communautaires (ex: NvDigitalVibranceControls). Sans carte NVIDIA ou sans
// nvapi64.dll : IsAvailable=false, l'app retombe sur la gamma ramp seule.
public sealed class NvidiaVibranceService
{
    private const uint IdInitialize = 0x0150E828;
    private const uint IdEnumNvidiaDisplayHandle = 0x9ABDD40D;
    private const uint IdGetDvcInfoEx = 0x0E45002D;
    private const uint IdSetDvcLevelEx = 0x4A82C2B1;
    private const uint IdGetHueInfo = 0x95B64341;
    private const uint IdSetHueAngle = 0xF5A0F22C;

    // NVAPI ne rapporte pas de min/max pour la teinte (juste l'angle courant et celui par
    // défaut) — plage conservatrice, une rotation complète de la roue chromatique.
    private const int HueAngleRange = 180;

    [StructLayout(LayoutKind.Sequential)]
    private struct NvDisplayDvcInfoEx
    {
        public uint Version;
        public int CurrentLevel;
        public int MinLevel;
        public int MaxLevel;
        public int DefaultLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvDisplayHueInfo
    {
        public uint Version;
        public int CurrentHueAngle;
        public int DefaultHueAngle;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int InitializeDelegate();
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EnumDisplayHandleDelegate(int thisEnum, out int pNvDispHandle);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetDvcInfoExDelegate(int hNvDisplay, int outputId, ref NvDisplayDvcInfoEx info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetDvcLevelExDelegate(int hNvDisplay, int outputId, ref NvDisplayDvcInfoEx info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int GetHueInfoDelegate(int hNvDisplay, int outputId, ref NvDisplayHueInfo info);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetHueAngleDelegate(int hNvDisplay, int outputId, int level);

    [DllImport("nvapi64.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "nvapi_QueryInterface")]
    private static extern IntPtr NvApiQueryInterface(uint interfaceId);

    private readonly List<int> _displayHandles = new();
    private GetDvcInfoExDelegate? _getDvc;
    private SetDvcLevelExDelegate? _setDvc;
    private GetHueInfoDelegate? _getHue;
    private SetHueAngleDelegate? _setHue;

    public bool IsAvailable { get; }
    public int MinLevel { get; private set; }
    public int MaxLevel { get; private set; }
    public int DefaultLevel { get; private set; }

    public bool HasHue { get; private set; }
    public int DefaultHueAngle { get; private set; }

    public NvidiaVibranceService()
    {
        try { IsAvailable = TryInitialize(); }
        catch { IsAvailable = false; }
    }

    private bool TryInitialize()
    {
        var initPtr = NvApiQueryInterface(IdInitialize);
        if (initPtr == IntPtr.Zero) return false;
        var init = Marshal.GetDelegateForFunctionPointer<InitializeDelegate>(initPtr);
        if (init() != 0) return false; // NVAPI_OK == 0

        var enumPtr = NvApiQueryInterface(IdEnumNvidiaDisplayHandle);
        var getPtr = NvApiQueryInterface(IdGetDvcInfoEx);
        var setPtr = NvApiQueryInterface(IdSetDvcLevelEx);
        if (enumPtr == IntPtr.Zero || getPtr == IntPtr.Zero || setPtr == IntPtr.Zero) return false;

        var enumHandle = Marshal.GetDelegateForFunctionPointer<EnumDisplayHandleDelegate>(enumPtr);
        _getDvc = Marshal.GetDelegateForFunctionPointer<GetDvcInfoExDelegate>(getPtr);
        _setDvc = Marshal.GetDelegateForFunctionPointer<SetDvcLevelExDelegate>(setPtr);

        for (int i = 0; i < 16; i++)
        {
            if (enumHandle(i, out int handle) != 0) break; // fin d'énumération ou erreur
            _displayHandles.Add(handle);
        }
        if (_displayHandles.Count == 0) return false;

        var info = new NvDisplayDvcInfoEx { Version = (uint)(Marshal.SizeOf<NvDisplayDvcInfoEx>() | 0x10000) };
        if (_getDvc(_displayHandles[0], 0, ref info) != 0) return false;

        MinLevel = info.MinLevel;
        MaxLevel = info.MaxLevel;
        DefaultLevel = info.DefaultLevel;

        // La teinte est une fonction NVAPI distincte et plus rarement supportée que la
        // vibrance — son absence ne doit pas faire échouer l'init de la vibrance elle-même.
        var getHuePtr = NvApiQueryInterface(IdGetHueInfo);
        var setHuePtr = NvApiQueryInterface(IdSetHueAngle);
        if (getHuePtr != IntPtr.Zero && setHuePtr != IntPtr.Zero)
        {
            _getHue = Marshal.GetDelegateForFunctionPointer<GetHueInfoDelegate>(getHuePtr);
            _setHue = Marshal.GetDelegateForFunctionPointer<SetHueAngleDelegate>(setHuePtr);
            var hueInfo = new NvDisplayHueInfo { Version = (uint)(Marshal.SizeOf<NvDisplayHueInfo>() | 0x10000) };
            if (_getHue(_displayHandles[0], 0, ref hueInfo) == 0)
            {
                HasHue = true;
                DefaultHueAngle = hueInfo.DefaultHueAngle;
            }
        }

        return true;
    }

    public int HueMin => -HueAngleRange;
    public int HueMax => HueAngleRange;

    public bool SetHue(int angle)
    {
        if (!HasHue || _setHue == null) return false;
        int clamped = Math.Clamp(angle, -HueAngleRange, HueAngleRange);
        bool allOk = true;
        foreach (var handle in _displayHandles)
            allOk &= _setHue(handle, 0, clamped) == 0;
        return allOk;
    }

    public void RestoreHueDefault()
    {
        if (HasHue) SetHue(DefaultHueAngle);
    }

    public bool SetLevel(int level)
    {
        if (!IsAvailable || _setDvc == null || _getDvc == null) return false;
        bool allOk = true;
        foreach (var handle in _displayHandles)
        {
            var info = new NvDisplayDvcInfoEx { Version = (uint)(Marshal.SizeOf<NvDisplayDvcInfoEx>() | 0x10000) };
            _getDvc(handle, 0, ref info); // rafraîchit min/max/default pour cet écran avant d'écrire
            info.CurrentLevel = Math.Clamp(level, info.MinLevel, info.MaxLevel);
            allOk &= _setDvc(handle, 0, ref info) == 0;
        }
        return allOk;
    }

    public void RestoreDefault()
    {
        if (IsAvailable) SetLevel(DefaultLevel);
    }
}
