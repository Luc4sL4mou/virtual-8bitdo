using System.Windows;
using Virtual8BitDo.App.Services;

namespace Virtual8BitDo.App.Views;

public partial class AntiCheatDialog : Window
{
    private AntiCheatDialog() => InitializeComponent();

    /// <summary>Mostra o aviso, a menos que o usuário tenha marcado "não mostrar novamente" antes.</summary>
    public static void ShowIfNeeded(Window owner, AppSettings settings)
    {
        if (settings.HideAntiCheatWarning) return;
        var dialog = new AntiCheatDialog { Owner = owner };
        dialog.ShowDialog();
        if (dialog.DontShowAgain.IsChecked == true)
        {
            settings.HideAntiCheatWarning = true;
            settings.Save();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => Close();
}
