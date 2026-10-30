using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using EkipVisual.Models;
using EkipVisual.Services;

namespace EkipVisual.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly GammaRampService _gamma;
    private readonly VibranceService _vibrance;
    private readonly SettingsService _settingsService;
    private readonly AppSettings _settings;
    private bool _suppressSideEffects = true;

    public ObservableCollection<ColorProfile> AllProfiles { get; } = new();

    [ObservableProperty] private double brightness;
    [ObservableProperty] private double contrast;
    [ObservableProperty] private double gamma = 1.0;
    [ObservableProperty] private double warmth;
    [ObservableProperty] private int vibrance;
    [ObservableProperty] private int hue;

    [ObservableProperty] private double gammaRed = 1.0;
    [ObservableProperty] private double gammaGreen = 1.0;
    [ObservableProperty] private double gammaBlue = 1.0;

    public bool HasRealVibrance => _vibrance.IsAvailable;
    public string VibranceVendorName => _vibrance.VendorName;
    public int VibranceMin => _vibrance.MinLevel;
    public int VibranceMax => _vibrance.MaxLevel;

    public bool HasHue => _vibrance.HasHue;
    public int HueMin => _vibrance.HueMin;
    public int HueMax => _vibrance.HueMax;

    [ObservableProperty] private string statusText = "";
    [ObservableProperty] private bool isAvailable;
    [ObservableProperty] private bool effectEnabled = true;

    [ObservableProperty] private bool launchWithWindows;

    [ObservableProperty] private bool isAddingProfile;
    [ObservableProperty] private string newProfileName = "";

    public MainViewModel(GammaRampService gamma, VibranceService vibrance, SettingsService settingsService)
    {
        _gamma = gamma;
        _vibrance = vibrance;
        _settingsService = settingsService;
        _settings = _settingsService.Load();

        IsAvailable = _gamma.IsAvailable;
        StatusText = IsAvailable
            ? $"Prêt — {_gamma.MonitorCount} écran(s) détecté(s)."
            : "Impossible d'accéder à la gamma ramp de l'écran (pilote graphique non compatible).";
        if (HasRealVibrance) StatusText += $" Vraie vibrance {VibranceVendorName} disponible.";

        RebuildProfileList();

        Brightness = _settings.Brightness;
        Contrast = _settings.Contrast;
        Gamma = _settings.Gamma;
        Warmth = _settings.Warmth;
        Vibrance = _settings.Vibrance ?? _vibrance.DefaultLevel;
        Hue = _settings.Hue ?? _vibrance.DefaultHueAngle;
        GammaRed = _settings.GammaRed;
        GammaGreen = _settings.GammaGreen;
        GammaBlue = _settings.GammaBlue;
        LaunchWithWindows = _settings.LaunchWithWindows;

        _suppressSideEffects = false;

        ApplyCurrent(); // ré-applique le dernier réglage tout de suite

        StartupService.SetEnabled(LaunchWithWindows); // aligne le registre sur le réglage sauvegardé
    }

    private void RebuildProfileList()
    {
        AllProfiles.Clear();
        foreach (var p in ColorProfile.Presets) AllProfiles.Add(p);
        foreach (var p in _settings.CustomProfiles) AllProfiles.Add(p);
    }

    partial void OnBrightnessChanged(double value) => ApplyAndPersist();
    partial void OnContrastChanged(double value) => ApplyAndPersist();
    partial void OnGammaChanged(double value) => ApplyAndPersist();
    partial void OnWarmthChanged(double value) => ApplyAndPersist();
    partial void OnVibranceChanged(int value) => ApplyAndPersist();
    partial void OnHueChanged(int value) => ApplyAndPersist();
    partial void OnGammaRedChanged(double value) => ApplyAndPersist();
    partial void OnGammaGreenChanged(double value) => ApplyAndPersist();
    partial void OnGammaBlueChanged(double value) => ApplyAndPersist();

    private void ApplyAndPersist()
    {
        if (_suppressSideEffects) return;
        ApplyCurrent();
        _settings.Brightness = Brightness;
        _settings.Contrast = Contrast;
        _settings.Gamma = Gamma;
        _settings.Warmth = Warmth;
        _settings.Vibrance = Vibrance;
        _settings.Hue = Hue;
        _settings.GammaRed = GammaRed;
        _settings.GammaGreen = GammaGreen;
        _settings.GammaBlue = GammaBlue;
        _settingsService.Save(_settings);
    }

    private void ApplyCurrent()
    {
        if (!IsAvailable || !EffectEnabled) return;
        bool ok = _gamma.Apply(Brightness, Contrast, Gamma, Warmth, GammaRed, GammaGreen, GammaBlue);
        if (HasRealVibrance) _vibrance.SetLevel(Vibrance);
        if (HasHue) _vibrance.SetHue(Hue);
        StatusText = ok ? "Appliqué." : "Le pilote graphique a refusé ce réglage — essaie des valeurs moins extrêmes.";
    }

    [RelayCommand]
    private void ApplyPreset(ColorProfile preset)
    {
        EffectEnabled = true;
        Brightness = preset.Brightness;
        Contrast = preset.Contrast;
        Gamma = preset.Gamma;
        Warmth = preset.Warmth;
    }

    [RelayCommand]
    private void Reset()
    {
        Brightness = 0;
        Contrast = 0;
        Gamma = 1.0;
        Warmth = 0;
        GammaRed = 1.0;
        GammaGreen = 1.0;
        GammaBlue = 1.0;
        if (HasRealVibrance) Vibrance = _vibrance.DefaultLevel;
        if (HasHue) Hue = _vibrance.DefaultHueAngle;
        if (IsAvailable) _gamma.RestoreOriginal();
        if (HasRealVibrance) _vibrance.RestoreDefault();
        if (HasHue) _vibrance.RestoreHueDefault();
        StatusText = "Réinitialisé.";
    }

    [RelayCommand]
    private void ToggleEffect()
    {
        if (EffectEnabled)
        {
            EffectEnabled = false;
            _gamma.RestoreOriginal();
            if (HasRealVibrance) _vibrance.RestoreDefault();
            if (HasHue) _vibrance.RestoreHueDefault();
            StatusText = "Effet désactivé (Ctrl+Alt+E pour réactiver).";
        }
        else
        {
            EffectEnabled = true;
            ApplyCurrent();
            StatusText = "Effet réactivé.";
        }
    }

    [RelayCommand]
    private void StartAddProfile() => IsAddingProfile = true;

    [RelayCommand]
    private void ConfirmAddProfile()
    {
        var name = NewProfileName.Trim();
        if (string.IsNullOrEmpty(name)) return;
        var profile = new ColorProfile(name, Brightness, Contrast, Gamma, Warmth) { IsCustom = true };
        _settings.CustomProfiles.Add(profile);
        _settingsService.Save(_settings);
        AllProfiles.Add(profile);
        NewProfileName = "";
        IsAddingProfile = false;
        StatusText = $"Profil « {name} » enregistré.";
    }

    [RelayCommand]
    private void CancelAddProfile()
    {
        NewProfileName = "";
        IsAddingProfile = false;
    }

    [RelayCommand]
    private void DeleteProfile(ColorProfile profile)
    {
        if (!profile.IsCustom) return;
        _settings.CustomProfiles.RemoveAll(p => p.Name == profile.Name);
        _settingsService.Save(_settings);
        AllProfiles.Remove(profile);
    }

    partial void OnLaunchWithWindowsChanged(bool value)
    {
        if (_suppressSideEffects) return;
        StartupService.SetEnabled(value);
        _settings.LaunchWithWindows = value;
        _settingsService.Save(_settings);
    }

    public void SaveSettings() => _settingsService.Save(_settings);
}
