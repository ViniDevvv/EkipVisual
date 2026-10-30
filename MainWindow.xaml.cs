using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using EkipVisual.Services;
using EkipVisual.ViewModels;

namespace EkipVisual;

public partial class MainWindow : Window
{
    private HotkeyService? _hotkey;

    public MainWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _hotkey = new HotkeyService(this);
        _hotkey.Pressed += () =>
        {
            if (DataContext is MainViewModel vm) vm.ToggleEffectCommand.Execute(null);
        };
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        // Fermer la fenêtre (croix ou Alt+F4) quitte vraiment l'app — le filtre doit disparaître
        // avec elle, pas rester actif en arrière-plan sans que l'utilisateur le sache.
        _hotkey?.Dispose();
        base.OnClosing(e);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Discord_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo { FileName = "https://discord.gg/YUKQRUj9Qs", UseShellExecute = true });
}
