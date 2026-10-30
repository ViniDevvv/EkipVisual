namespace EkipVisual.Models;

public sealed record ColorProfile(string Name, double Brightness, double Contrast, double Gamma, double Warmth)
{
    public bool IsCustom { get; init; }

    public static ColorProfile Default => new("Défaut", 0, 0, 1.0, 0);

    public static readonly ColorProfile[] Presets =
    [
        Default,
        new("Été", Brightness: 8, Contrast: 10, Gamma: 1.05, Warmth: 15),
        new("Hiver", Brightness: 0, Contrast: 8, Gamma: 0.95, Warmth: -20),
        new("Désert", Brightness: 10, Contrast: 15, Gamma: 1.1, Warmth: 30),
        new("Nuit", Brightness: 20, Contrast: -10, Gamma: 1.3, Warmth: -5),
    ];
}
