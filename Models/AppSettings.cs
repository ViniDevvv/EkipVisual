namespace EkipVisual.Models;

public sealed class AppSettings
{
    public double Brightness { get; set; }
    public double Contrast { get; set; }
    public double Gamma { get; set; } = 1.0;
    public double Warmth { get; set; }
    public int? Vibrance { get; set; } // null = jamais réglé -> valeur par défaut du pilote au 1er lancement
    public int? Hue { get; set; } // idem
    public double GammaRed { get; set; } = 1.0;
    public double GammaGreen { get; set; } = 1.0;
    public double GammaBlue { get; set; } = 1.0;
    public bool LaunchWithWindows { get; set; }
    public List<ColorProfile> CustomProfiles { get; set; } = new();
}
