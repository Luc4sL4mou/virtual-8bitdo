using System.Windows;

namespace Virtual8BitDo.App.Services;

/// <summary>
/// Tema escuro (padrão) ou claro. Troca na hora: o tema Fluent dos controles do Windows,
/// as cores do app (Themes/Colors.*.xaml), a barra de título de todas as janelas e o desenho do controle.
/// </summary>
public static class ThemeManager
{
    private const string ColorsPrefix = "Themes/Colors.";

    public static bool IsDark { get; private set; } = true;

    /// <summary>O tema mudou (quem pinta por código, como o desenho do controle, redesenha).</summary>
    public static event Action? Changed;

    public static void Apply(bool dark)
    {
        var app = Application.Current;
        IsDark = dark;
        app.ThemeMode = dark ? ThemeMode.Dark : ThemeMode.Light;

        // As cores do app entram por último, para valerem por cima do tema Fluent (inclusive a cor de destaque).
        var dicts = app.Resources.MergedDictionaries;
        foreach (var old in dicts.Where(d => d.Source?.OriginalString.Contains(ColorsPrefix) == true).ToList())
            dicts.Remove(old);
        dicts.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/{ColorsPrefix}{(dark ? "Dark" : "Light")}.xaml") });

        foreach (Window w in app.Windows) WindowEffects.Apply(w);
        Changed?.Invoke();
    }
}
