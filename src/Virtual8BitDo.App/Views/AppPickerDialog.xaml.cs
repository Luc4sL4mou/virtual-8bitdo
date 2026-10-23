using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Virtual8BitDo.App.Services;

namespace Virtual8BitDo.App.Views;

public partial class AppPickerDialog : Window
{
    /// <summary>Linha da lista: o programa e, se for o caso, o perfil que já usa esse .exe.</summary>
    public sealed record Item(RunningApp App, string? ExistingProfileName)
    {
        public string Details => string.IsNullOrWhiteSpace(App.WindowTitle) || App.WindowTitle == App.DisplayName
            ? App.Exe : $"{App.Exe}  ·  {App.WindowTitle}";
        public string? ExistingProfile => ExistingProfileName == null ? null : $"Já tem perfil: {ExistingProfileName} (ele passará para o novo)";
        public bool HasExistingProfile => ExistingProfileName != null;
    }

    private readonly Func<IEnumerable<string>> _recentExes;
    private readonly Func<string, string?> _profileForExe;
    private List<Item> _all = new();

    /// <summary>Programa escolhido; null = "Perfil sem jogo".</summary>
    public RunningApp? Chosen { get; private set; }

    private AppPickerDialog(string heading, string confirmText, bool allowNoGame,
                            Func<IEnumerable<string>> recentExes, Func<string, string?> profileForExe)
    {
        InitializeComponent();
        Heading.Text = heading;
        Title = heading;
        ConfirmButton.Content = confirmText;
        NoGameButton.Visibility = allowNoGame ? Visibility.Visible : Visibility.Collapsed;
        _recentExes = recentExes;
        _profileForExe = profileForExe;
        Loaded += (_, _) => { Reload(); Search.Focus(); };
    }

    /// <summary>
    /// Mostra a lista. Retorna (true, app) ao confirmar; (true, null) para "Perfil sem jogo"; (false, null) se cancelar.
    /// </summary>
    public static (bool Ok, RunningApp? App) Show(Window owner, string heading, string confirmText, bool allowNoGame,
                                                  Func<IEnumerable<string>> recentExes, Func<string, string?> profileForExe)
    {
        var d = new AppPickerDialog(heading, confirmText, allowNoGame, recentExes, profileForExe) { Owner = owner };
        bool ok = d.ShowDialog() == true;
        return (ok, ok ? d.Chosen : null);
    }

    private void Reload()
    {
        _all = RunningApps.List(_recentExes()).Select(a => new Item(a, _profileForExe(a.Exe))).ToList();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        string q = Search.Text.Trim();
        var items = q.Length == 0 ? _all : _all.Where(i =>
            i.App.DisplayName.Contains(q, StringComparison.CurrentCultureIgnoreCase) ||
            i.App.Exe.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            i.App.WindowTitle.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
        Apps.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count > 0 && Apps.SelectedItem == null) Apps.SelectedIndex = 0;
    }

    private void Search_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();
    private void Refresh_Click(object sender, RoutedEventArgs e) => Reload();
    private void Apps_SelectionChanged(object sender, SelectionChangedEventArgs e) => ConfirmButton.IsEnabled = Apps.SelectedItem != null;

    private void Apps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (Apps.SelectedItem is Item) Confirm_Click(sender, e);
    }

    private void Confirm_Click(object sender, RoutedEventArgs e)
    {
        if (Apps.SelectedItem is not Item item) return;
        Chosen = item.App;
        DialogResult = true;
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Escolha o executável do jogo", Filter = "Programas (*.exe)|*.exe" };
        if (dlg.ShowDialog(this) != true) return;
        Chosen = RunningApps.FromPath(dlg.FileName);
        DialogResult = true;
    }

    private void NoGame_Click(object sender, RoutedEventArgs e)
    {
        Chosen = null;
        DialogResult = true;
    }
}
