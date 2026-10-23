using Virtual8BitDo.Core.Profiles;
using Virtual8BitDo.Input;
using Virtual8BitDo.Output;

namespace Virtual8BitDo.App.ViewModels;

/// <summary>Item de lista com um valor e o texto exibido.</summary>
public sealed record Option<T>(T Value, string Label);

/// <summary>Nomes amigáveis dos botões, usados no editor e nas listas.</summary>
public static class ButtonLabels
{
    private static readonly Dictionary<string, string> Names = new()
    {
        [StandardButtons.Back] = "Select (−)", [StandardButtons.Start] = "Start (+)", [StandardButtons.Guide] = "Home",
        [StandardButtons.DUp] = "D-pad ↑", [StandardButtons.DDown] = "D-pad ↓",
        [StandardButtons.DLeft] = "D-pad ←", [StandardButtons.DRight] = "D-pad →",
        // Botões extras: nomes do 8BitDo Ultimate 2 (o catálogo pode ter outros nomes para outros modelos).
        [StandardButtons.P1] = "R4", [StandardButtons.P2] = "L4", [StandardButtons.P3] = "PR", [StandardButtons.P4] = "PL",
    };

    public static string For(string id, ControllerViewModel? controller = null) =>
        Profile.IsExtra(id) && controller != null ? controller.ExtraLabel(id) : Names.GetValueOrDefault(id, id);
}

/// <summary>
/// Editor do botão clicado no desenho: o que ele faz (padrão, nada, botão Xbox, tecla) e se tem turbo.
/// Cada mudança é aplicada e salva na hora.
/// </summary>
public sealed class ButtonEditorViewModel : ObservableObject
{
    private readonly Profile _profile;
    private readonly Action _onChanged;
    private ActionKind _kind;
    private string? _xbox1, _xbox2, _key;
    private bool _turbo;

    public ButtonEditorViewModel(Profile profile, string buttonId, ControllerViewModel? controller, Action onChanged)
    {
        _profile = profile;
        _onChanged = onChanged;
        ButtonId = buttonId;
        Title = ButtonLabels.For(buttonId, controller);
        IsExtra = Profile.IsExtra(buttonId);
        IsAvailableNow = controller?.IsAvailable(buttonId) ?? true;

        KindOptions = IsExtra
            ? new[] { new Option<ActionKind>(ActionKind.Default, "Sem função"), new(ActionKind.Xbox, "Botão do Xbox"), new(ActionKind.Key, "Tecla do teclado") }
            : new[] { new Option<ActionKind>(ActionKind.Default, $"Padrão ({Title})"), new(ActionKind.None, "Desativado"), new(ActionKind.Xbox, "Outro botão do Xbox"), new(ActionKind.Key, "Tecla do teclado") };

        var a = profile.Get(buttonId);
        _kind = IsExtra && a.Kind == ActionKind.None ? ActionKind.Default : a.Kind;
        _xbox1 = a.Xbox.ElementAtOrDefault(0) ?? StandardButtons.A;
        _xbox2 = a.Xbox.ElementAtOrDefault(1);
        _key = a.Key ?? "F13";
        _turbo = a.Turbo;
    }

    public string ButtonId { get; }
    public string Title { get; }
    public bool IsExtra { get; }

    /// <summary>False quando o controle selecionado (no modo atual) não envia este botão ao PC.</summary>
    public bool IsAvailableNow { get; }
    public bool IsUnavailableNow => !IsAvailableNow;

    public IReadOnlyList<Option<ActionKind>> KindOptions { get; }

    public static IReadOnlyList<Option<string?>> XboxOptions { get; } =
        Profile.XboxTargets.Select(id => new Option<string?>(id, ButtonLabels.For(id))).ToList();

    public static IReadOnlyList<Option<string?>> SecondXboxOptions { get; } =
        new[] { new Option<string?>(null, "— (nenhum)") }.Concat(XboxOptions).ToList();

    public static IReadOnlyList<string> KeyOptions { get; } = KeyboardOutput.Keys.Keys.ToList();

    public ActionKind Kind
    {
        get => _kind;
        set { if (Set(ref _kind, value)) { OnPropertyChanged(nameof(IsXbox)); OnPropertyChanged(nameof(IsKey)); Save(); } }
    }

    public bool IsXbox => Kind == ActionKind.Xbox;
    public bool IsKey => Kind == ActionKind.Key;

    public string? Xbox1 { get => _xbox1; set { if (Set(ref _xbox1, value)) Save(); } }
    public string? Xbox2 { get => _xbox2; set { if (Set(ref _xbox2, value)) Save(); } }
    public string? Key { get => _key; set { if (Set(ref _key, value)) Save(); } }
    public bool Turbo { get => _turbo; set { if (Set(ref _turbo, value)) Save(); } }

    /// <summary>Atualiza o turbo quando ele foi mudado pelo atalho no controle (sem salvar de novo).</summary>
    public void RefreshTurbo()
    {
        _turbo = _profile.Get(ButtonId).Turbo;
        OnPropertyChanged(nameof(Turbo));
    }

    private void Save()
    {
        var action = new ButtonAction { Kind = Kind, Turbo = Turbo };
        if (Kind == ActionKind.Xbox)
        {
            if (Xbox1 != null) action.Xbox.Add(Xbox1);
            if (Xbox2 != null && Xbox2 != Xbox1) action.Xbox.Add(Xbox2);
        }
        if (Kind == ActionKind.Key) action.Key = Key;
        _profile.Set(ButtonId, action);
        _onChanged();
    }
}
