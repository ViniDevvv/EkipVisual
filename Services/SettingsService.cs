using System.IO;
using System.Text.Json;
using EkipVisual.Models;

namespace EkipVisual.Services;

public sealed class SettingsService
{
    private static readonly string FolderPath =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EkipVisual");
    private static readonly string FilePath = Path.Combine(FolderPath, "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null) return settings;
            }
        }
        catch
        {
            // Fichier corrompu ou illisible : on repart sur des réglages neutres plutôt que
            // de bloquer le lancement de l'app pour un fichier de config cassé.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        try
        {
            Directory.CreateDirectory(FolderPath);
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(FilePath, json);
        }
        catch
        {
            // Sauvegarde best-effort : un échec ici ne doit jamais interrompre l'usage de l'app.
        }
    }
}
