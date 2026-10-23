using System.Collections.ObjectModel;
using Virtual8BitDo.App.Services;
using Virtual8BitDo.Core;
using Virtual8BitDo.Core.Mapping;
using Virtual8BitDo.Core.Profiles;

namespace Virtual8BitDo.App.ViewModels;

/// <summary>
/// Perfis por jogo (etapa 5) e edição do mapeamento (etapa 6):
///  - "Em uso agora": o perfil do jogo em foco (ou o padrão), enviado ao motor;
///  - "Editando": o perfil escolhido na lista, que você altera clicando no desenho.
/// Os dois podem ser o mesmo; qualquer mudança no perfil em uso vale na hora.
/// </summary>
public sealed class ProfilesViewModel : ObservableObject
{
    private readonly ProfileStore _store;
    private readonly ControllerEngine _engine;
    private readonly ForegroundWatcher _foreground;
    private readonly Func<ControllerViewModel?> _selectedController;
    private Profile _editing;
    private Profile _active;
    private string? _selectedButton;
    private ButtonEditorViewModel? _editor;
    private string? _gameName;
    private System.Windows.Media.ImageSource? _gameIcon;

    public ProfilesViewModel(ProfileStore store, ControllerEngine engine, ForegroundWatcher foreground, Func<ControllerViewModel?> selectedController)
    {
        _store = store;
        _engine = engine;
        _foreground = foreground;
        _selectedController = selectedController;
        Profiles = new ObservableCollection<Profile>(store.Profiles);
        _editing = _active = store.Default;
        _engine.SetProfile(_active);
        _foreground.ForegroundChanged += _ => OnForegroundChanged();
    }

    /// <summary>O jogo em foco trocou o perfil sozinho (para o aviso flutuante): perfil novo e nome do jogo (null = nenhum).</summary>
    public event Action<Profile, string?>? ActiveSwitchedByGame;

    public ObservableCollection<Profile> Profiles { get; }

    // ---- Perfil em uso (pelo jogo em foco) ----

    public Profile Active => _active;
    public string ActiveName => _active.Name;

    /// <summary>
    /// Explica por que o perfil está em uso: o jogo em foco tem perfil próprio, ou não tem e vale o Padrão.
    /// (A janela do Virtual 8BitDo e as do Windows não contam como "programa em foco".)
    /// </summary>
    public string ActiveText =>
        !_active.IsDefault ? $"Perfil \"{_active.Name}\" ativado automaticamente"
        : _foreground.CurrentExe != null ? "Sem perfil próprio: usando o Padrão"
        : "Abra um jogo: o perfil dele entra sozinho";

    // ---- Jogo em foco (cartão do topo) ----

    public bool HasGame => _foreground.CurrentExe != null;
    public string GameName => _gameName ?? "Nenhum jogo em foco";
    public string? GameExe => _foreground.CurrentExe;
    public System.Windows.Media.ImageSource? GameIcon => _gameIcon;
    public bool GameHasProfile => !_active.IsDefault;

    /// <summary>Jogo em foco sem perfil próprio: o cartão oferece criar um com um clique.</summary>
    public bool CanCreateForGame => HasGame && _active.IsDefault;

    private void OnForegroundChanged()
    {
        if (_foreground.CurrentPath is { } path) (_gameName, _gameIcon) = RunningApps.Describe(path, _foreground.CurrentTitle);
        else (_gameName, _gameIcon) = (null, null);
        foreach (var n in new[] { nameof(HasGame), nameof(GameName), nameof(GameExe), nameof(GameIcon) })
            OnPropertyChanged(n);

        var before = _active;
        UpdateActive();
        if (_active != before) ActiveSwitchedByGame?.Invoke(_active, _gameName);
    }

    /// <summary>Cria (e já ativa) um perfil para o jogo em foco agora.</summary>
    public void NewForCurrentGame()
    {
        if (_foreground.CurrentPath is not { } path) return;
        New(RunningApps.FromPath(path) with { DisplayName = GameName });
    }

    private void UpdateActive()
    {
        var p = _store.ForExe(_foreground.CurrentExe);
        if (p != _active)
        {
            _active = p;
            _engine.SetProfile(p);
            OnPropertyChanged(nameof(Active));
        }
        OnPropertyChanged(nameof(ActiveText));
        OnPropertyChanged(nameof(ActiveName));
        OnPropertyChanged(nameof(GameHasProfile));
        OnPropertyChanged(nameof(CanCreateForGame));
    }

    // ---- Perfil em edição ----

    public Profile Editing
    {
        get => _editing;
        set
        {
            if (value == null || !Set(ref _editing, value)) return;
            foreach (var n in new[] { nameof(EditingName), nameof(IsEditingDefault), nameof(CanChangeExe), nameof(TurboHz) })
                OnPropertyChanged(n);
            RefreshGameFields();
            RebuildEditor();
        }
    }

    public bool IsEditingDefault => _editing.IsDefault;
    public bool CanChangeExe => !_editing.IsDefault;

    public string EditingName
    {
        get => _editing.Name;
        set
        {
            _store.Rename(_editing, value);
            OnPropertyChanged();
            RefreshList();
            OnPropertyChanged(nameof(ActiveText));
            OnPropertyChanged(nameof(ActiveName));
        }
    }

    /// <summary>Jogo (.exe) associado ao perfil em edição.</summary>
    public string? EditingExe => _editing.Exe;

    /// <summary>Texto do campo "Jogo": o .exe, ou explica que o perfil não troca sozinho.</summary>
    public string EditingGameText => _editing.IsDefault ? "—"
        : _editing.Exe ?? "Nenhum (perfil manual: não é ativado sozinho)";

    public bool HasEditingExe => _editing.Exe != null;

    /// <summary>Programas que estiveram em foco: os mais recentes aparecem primeiro na lista de escolha.</summary>
    public IReadOnlyCollection<string> RecentExes => _foreground.RecentExes;

    /// <summary>Nome do perfil que já usa este .exe (para avisar na lista de escolha), ou null.</summary>
    public string? ProfileNameForExe(string exe) =>
        _store.Profiles.FirstOrDefault(p => !p.IsDefault && string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase))?.Name;

    /// <summary>Cria um perfil para o jogo escolhido na lista (ou sem jogo, se <paramref name="app"/> for null).</summary>
    public void New(RunningApp? app)
    {
        var p = _store.Create(app?.DisplayName ?? "Novo perfil", null);
        if (app != null) _store.SetExe(p, app.Exe); // se outro perfil usava esse jogo, ele perde a associação
        RefreshList();
        Editing = p;
        UpdateActive();
    }

    /// <summary>Troca (ou remove, com null) o jogo do perfil em edição.</summary>
    public void SetGame(RunningApp? app)
    {
        _store.SetExe(_editing, app?.Exe);
        RefreshGameFields();
        UpdateActive();
    }

    private void RefreshGameFields()
    {
        OnPropertyChanged(nameof(EditingExe));
        OnPropertyChanged(nameof(EditingGameText));
        OnPropertyChanged(nameof(HasEditingExe));
    }

    public void Duplicate()
    {
        var p = _store.Duplicate(_editing);
        RefreshList();
        Editing = p;
    }

    public void Delete()
    {
        if (_editing.IsDefault) return;
        _store.Delete(_editing);
        RefreshList();
        Editing = _store.Default;
        UpdateActive();
    }

    public string FolderPath => _store.Folder;

    private void RefreshList()
    {
        var editing = _editing;
        Profiles.Clear();
        foreach (var p in _store.Profiles) Profiles.Add(p);
        _editing = editing;
        OnPropertyChanged(nameof(Editing));
    }

    // ---- Turbo ----

    public int TurboHz
    {
        get => _editing.TurboHz;
        set
        {
            value = Math.Clamp(value, Profile.MinTurboHz, Profile.MaxTurboHz);
            if (_editing.TurboHz == value) return;
            _editing.TurboHz = value;
            OnPropertyChanged();
            ProfileChanged();
        }
    }

    /// <summary>Atalho no controle mudou o turbo: aplica no perfil em uso (o motor já aplicou na cópia dele) e salva.</summary>
    public void ApplyFromController(TurboChange change)
    {
        _active.TurboHz = change.Hz;
        if (change.Button != null && change.Enabled is bool on)
        {
            var a = _active.Get(change.Button).Clone();
            a.Turbo = on;
            _active.Set(change.Button, a);
        }
        _store.Save(_active);
        if (_editing == _active)
        {
            OnPropertyChanged(nameof(TurboHz));
            _editor?.RefreshTurbo();
        }
        DiagramNeedsRefresh?.Invoke();
    }

    // ---- Editor do botão clicado ----

    /// <summary>O desenho precisa ser redesenhado (seleção ou mapeamento mudou).</summary>
    public event Action? DiagramNeedsRefresh;

    public string? SelectedButton
    {
        get => _selectedButton;
        set { if (Set(ref _selectedButton, value)) RebuildEditor(); }
    }

    public ButtonEditorViewModel? Editor
    {
        get => _editor;
        private set { Set(ref _editor, value); OnPropertyChanged(nameof(HasEditor)); OnPropertyChanged(nameof(HasNoEditor)); }
    }

    public bool HasEditor => _editor != null;
    public bool HasNoEditor => _editor == null;

    public void RebuildEditor()
    {
        Editor = _selectedButton == null ? null
            : new ButtonEditorViewModel(_editing, _selectedButton, _selectedController(), ProfileChanged);
        DiagramNeedsRefresh?.Invoke();
    }

    /// <summary>Decoração do desenho: o botão foi alterado no perfil em edição? Tem turbo?</summary>
    public (bool Customized, bool Turbo) Decoration(string id)
    {
        if (!_editing.Buttons.TryGetValue(id, out var a)) return (false, false);
        return (a.Kind != ActionKind.Default, a.Turbo);
    }

    private void ProfileChanged()
    {
        _store.Save(_editing);
        if (_editing == _active) _engine.SetProfile(_active);
        DiagramNeedsRefresh?.Invoke();
    }
}
