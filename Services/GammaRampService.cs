using System.Runtime.InteropServices;

namespace EkipVisual.Services;

// Ajuste l'affichage via la gamma ramp Windows (GDI), sans toucher au jeu ni à ses fichiers —
// aucune interaction avec l'anti-cheat possible puisqu'on ne touche jamais le process du jeu.
// Applique le même réglage à TOUS les écrans connectés (un DC par adaptateur via
// EnumDisplayDevices/CreateDC, pas juste GetDC(NULL) qui ne cible fiablement que l'écran
// principal). Limite mathématique importante : une gamma ramp applique une courbe par canal
// indépendante, elle ne peut donc PAS faire de vraie saturation HSV (il faudrait connaître les
// 3 canaux d'un même pixel à la fois). D'où l'absence volontaire d'un curseur "saturation" —
// seuls Luminosité/Contraste/Gamma (symétriques, ne teintent jamais le gris) et Chaleur (teinte
// volontaire chaud/froid, comme f.lux/Night Light) sont proposés.
public sealed class GammaRampService : IDisposable
{
    private const int RampEntries = 256;
    private const uint DisplayDeviceAttachedToDesktop = 0x1;

    [StructLayout(LayoutKind.Sequential)]
    private struct Ramp
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RampEntries)] public ushort[] Red;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RampEntries)] public ushort[] Green;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = RampEntries)] public ushort[] Blue;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct DisplayDevice
    {
        public int cb;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string DeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceString;
        public uint StateFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceID;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string DeviceKey;
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool GetDeviceGammaRamp(IntPtr hDC, ref Ramp lpRamp);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool SetDeviceGammaRamp(IntPtr hDC, ref Ramp lpRamp);

    [DllImport("gdi32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CreateDC(string lpszDriver, string lpszDevice, string? lpszOutput, IntPtr lpInitData);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool EnumDisplayDevices(string? lpDevice, uint iDevNum, ref DisplayDevice lpDisplayDevice, uint dwFlags);

    private sealed class Monitor
    {
        public required IntPtr Hdc;
        public required string DeviceName;
        public Ramp Original;
        public bool HasOriginal;
    }

    private readonly List<Monitor> _monitors = new();

    public bool IsAvailable { get; }
    public int MonitorCount => _monitors.Count;

    public GammaRampService()
    {
        for (uint i = 0; i < 32; i++)
        {
            var dd = new DisplayDevice { cb = Marshal.SizeOf<DisplayDevice>() };
            if (!EnumDisplayDevices(null, i, ref dd, 0)) break;
            if ((dd.StateFlags & DisplayDeviceAttachedToDesktop) == 0) continue;

            var hdc = CreateDC(dd.DeviceName, dd.DeviceName, null, IntPtr.Zero);
            if (hdc == IntPtr.Zero) continue;

            var monitor = new Monitor { Hdc = hdc, DeviceName = dd.DeviceName };
            var ramp = NewRamp();
            if (GetDeviceGammaRamp(hdc, ref ramp))
            {
                monitor.Original = ramp;
                monitor.HasOriginal = true;
            }
            _monitors.Add(monitor);
        }

        IsAvailable = _monitors.Any(m => m.HasOriginal);
    }

    // brightness/contrast/warmth: -100..100 (0 = neutre) — gamma/gammaR/G/B: 0.4..2.5 (1.0 = neutre)
    public bool Apply(double brightness, double contrast, double gamma, double warmth, double gammaR = 1.0, double gammaG = 1.0, double gammaB = 1.0)
    {
        if (!IsAvailable) return false;
        var ramp = BuildRamp(brightness, contrast, gamma, warmth, gammaR, gammaG, gammaB);
        bool allOk = true;
        foreach (var m in _monitors.Where(m => m.HasOriginal))
        {
            var r = ramp;
            allOk &= SetDeviceGammaRamp(m.Hdc, ref r);
        }
        return allOk;
    }

    public bool RestoreOriginal()
    {
        bool allOk = true;
        foreach (var m in _monitors.Where(m => m.HasOriginal))
        {
            var r = m.Original;
            allOk &= SetDeviceGammaRamp(m.Hdc, ref r);
        }
        return allOk;
    }

    private static Ramp NewRamp() => new()
    {
        Red = new ushort[RampEntries],
        Green = new ushort[RampEntries],
        Blue = new ushort[RampEntries],
    };

    private static Ramp BuildRamp(double brightness, double contrast, double gamma, double warmth, double gammaR, double gammaG, double gammaB)
    {
        var ramp = NewRamp();

        double contrastFactor = (100.0 + Math.Clamp(contrast, -100, 100)) / 100.0;
        double brightnessOffset = Math.Clamp(brightness, -100, 100) / 200.0;
        double warm = Math.Clamp(warmth, -100, 100) / 100.0;

        // Gamma par canal = gamma général × balance du canal (balance=1.0 -> comportement
        // identique à un seul gamma partagé). Permet une vraie balance des couleurs façon
        // calibration (comme l'outil Windows natif), indépendante de la Chaleur ci-dessous.
        double invGammaR = 1.0 / Math.Clamp(gamma * gammaR, 0.2, 3.0);
        double invGammaG = 1.0 / Math.Clamp(gamma * gammaG, 0.2, 3.0);
        double invGammaB = 1.0 / Math.Clamp(gamma * gammaB, 0.2, 3.0);

        for (int i = 0; i < RampEntries; i++)
        {
            // Contraste autour du point milieu, puis luminosité additive — partagés entre
            // canaux, c'est le gamma ensuite qui diverge par canal.
            double baseX = (i / (double)(RampEntries - 1) - 0.5) * contrastFactor + 0.5 + brightnessOffset;
            baseX = Math.Clamp(baseX, 0.0, 1.0);

            double r = Math.Pow(baseX, invGammaR);
            double g = Math.Pow(baseX, invGammaG);
            double b = Math.Pow(baseX, invGammaB);

            // Chaleur : décalage égal et opposé sur rouge/bleu, vert inchangé — teinte
            // volontaire chaud/froid (pas une saturation), le gris reste gris à warmth=0.
            r = Math.Clamp(r + warm * 0.12, 0.0, 1.0);
            b = Math.Clamp(b - warm * 0.12, 0.0, 1.0);

            ramp.Red[i] = ToChannel(r);
            ramp.Green[i] = ToChannel(g);
            ramp.Blue[i] = ToChannel(b);
        }

        return ramp;
    }

    private static ushort ToChannel(double v) => (ushort)Math.Clamp(v * 65535.0, 0, 65535);

    public void Dispose()
    {
        foreach (var m in _monitors) DeleteDC(m.Hdc);
        _monitors.Clear();
    }
}
