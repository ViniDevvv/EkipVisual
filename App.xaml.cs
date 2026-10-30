using System.Windows;
using EkipVisual.Services;
using EkipVisual.ViewModels;
using Application = System.Windows.Application;
using SystemEvents = Microsoft.Win32.SystemEvents;

namespace EkipVisual;

public partial class App : Application
{
    // Instances uniques pour toute la durée de vie du process : c'est la gamma ramp qui garde
    // l'état d'origine, restauré sur TOUS les chemins de sortie (fermeture réelle, crash non
    // géré, arrêt du process, session Windows qui se termine) — laisser l'écran teinté après
    // un crash serait inacceptable.
    private GammaRampService? _gamma;
    private VibranceService? _vibrance;
    private TrayService? _tray;
    private MainWindow? _window;
    private MainViewModel? _viewModel;

    private void App_Startup(object sender, StartupEventArgs e)
    {
        _gamma = new GammaRampService();
        _vibrance = new VibranceService();

        void RestoreAll()
        {
            _gamma?.RestoreOriginal();
            _vibrance?.RestoreDefault();
        }

        // Filet de sécurité manuel : si jamais l'app est bloquée/verrouillée (ex: comme
        // aujourd'hui, exe verrouillé par un process qui tourne encore) on peut forcer un reset
        // de l'écran sans passer par l'UI, depuis n'importe quel terminal.
        if (e.Args.Contains("--reset-display"))
        {
            RestoreAll();
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, _) => RestoreAll();
        AppDomain.CurrentDomain.UnhandledException += (_, _) => RestoreAll();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => RestoreAll();
        // Fin de session Windows (déconnexion/redémarrage/arrêt) : ce n'est ni une fermeture de
        // fenêtre normale ni un crash, donc ni OnClosing ni ProcessExit ne sont garantis.
        SystemEvents.SessionEnding += (_, _) => RestoreAll();

        _viewModel = new MainViewModel(_gamma, _vibrance, new SettingsService());
        _window = new MainWindow { DataContext = _viewModel };
        MainWindow = _window;
        // Fermer la fenêtre (croix, Alt+F4, ou "Quitter" depuis la tray) quitte vraiment l'app —
        // le filtre ne doit jamais rester actif sans fenêtre visible pour le piloter.
        _window.Closed += (_, _) =>
        {
            _viewModel.SaveSettings();
            RestoreAll();
            _tray?.Dispose();
        };

        _tray = new TrayService();
        _tray.OpenRequested += () =>
        {
            _window.Show();
            _window.WindowState = WindowState.Normal;
            _window.Activate();
        };
        _tray.ExitRequested += () => _window.Close();

        // Toujours Show() pour garantir la création du handle natif (nécessaire au raccourci
        // global) même en démarrage minimisé — on cache juste la fenêtre juste après.
        _window.Show();
        if (e.Args.Contains("--minimized")) _window.Hide();
    }
}
