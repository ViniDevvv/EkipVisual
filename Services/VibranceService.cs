namespace EkipVisual.Services;

// Point d'entrée unique pour la vraie vibrance/teinte matérielle : essaie NVIDIA puis AMD,
// expose une interface unique peu importe le fabricant réellement présent. Sur Intel ou carte
// non supportée, IsAvailable reste false et l'app se contente de la gamma ramp comme avant.
public sealed class VibranceService : IDisposable
{
    private readonly NvidiaVibranceService _nvidia;
    private readonly AmdVibranceService? _amd;

    public bool IsAvailable { get; }
    public string VendorName { get; } = "";
    public int MinLevel { get; }
    public int MaxLevel { get; }
    public int DefaultLevel { get; }

    public bool HasHue { get; }
    public int HueMin { get; }
    public int HueMax { get; }
    public int DefaultHueAngle { get; }

    public VibranceService()
    {
        _nvidia = new NvidiaVibranceService();
        if (_nvidia.IsAvailable)
        {
            IsAvailable = true;
            VendorName = "NVIDIA";
            MinLevel = _nvidia.MinLevel;
            MaxLevel = _nvidia.MaxLevel;
            DefaultLevel = _nvidia.DefaultLevel;
            HasHue = _nvidia.HasHue;
            HueMin = _nvidia.HueMin;
            HueMax = _nvidia.HueMax;
            DefaultHueAngle = _nvidia.DefaultHueAngle;
            return;
        }

        _amd = new AmdVibranceService();
        if (_amd.IsAvailable)
        {
            IsAvailable = true;
            VendorName = "AMD";
            MinLevel = _amd.MinLevel;
            MaxLevel = _amd.MaxLevel;
            DefaultLevel = _amd.DefaultLevel;
            HasHue = _amd.HasHue;
            HueMin = _amd.HueMin;
            HueMax = _amd.HueMax;
            DefaultHueAngle = _amd.DefaultHueAngle;
        }
    }

    public bool SetLevel(int level)
    {
        if (_nvidia.IsAvailable) return _nvidia.SetLevel(level);
        if (_amd?.IsAvailable == true) return _amd.SetLevel(level);
        return false;
    }

    public void RestoreDefault()
    {
        if (_nvidia.IsAvailable) _nvidia.RestoreDefault();
        if (_amd?.IsAvailable == true) _amd.RestoreDefault();
    }

    public bool SetHue(int angle)
    {
        if (_nvidia.IsAvailable) return _nvidia.SetHue(angle);
        if (_amd?.IsAvailable == true) return _amd.SetHue(angle);
        return false;
    }

    public void RestoreHueDefault()
    {
        if (_nvidia.IsAvailable) _nvidia.RestoreHueDefault();
        if (_amd?.IsAvailable == true) _amd.RestoreHueDefault();
    }

    public void Dispose() => _amd?.Dispose();
}
