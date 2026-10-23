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

    public ProfilesViewModel(ProfileStore store, ControllerEngine engine, ForegroundWatcher foreground, Func<ControllerViewModel?> selectedController)
    {
        _store = store;
        _engine = engine;
        _foreground = foreground;
        _selectedController = selectedController;
        Profiles = new ObservableCollection<Profile>(store.Profiles);
        _editing = _active = store.Default;
        _engine.SetProfile(_active);
        _foreground.ForegroundChanged += _ => UpdateActive();
    }

    public ObservableCollection<Profile> Profiles { get; }

    // ---- Perfil em uso (pelo jogo em foco) ----

    public Profile Active => _active;

    public string ActiveText =>
        _foreground.CurrentExe is { } exe
            ? $"Em uso agora: {_active.Name}  ·  programa em foco: {exe}"
            : $"Em uso agora: {_active.Name}";

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
    }

    // ---- Perfil em edição ----

    public Profile Editing
    {
        get => _editing;
        set
        {
            if (value == null || !Set(ref _editing, value)) return;
            foreach (var n in new[] { nameof(EditingName), nameof(EditingExe), nameof(IsEditingDefault), nameof(CanChangeExe), nameof(TurboHz) })
                OnPropertyChanged(n);
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
        }
    }

    /// <summary>Jogo (.exe) associado ao perfil em edição.</summary>
    public string? EditingExe
    {
        get => _editing.Exe;
        set
        {
            _store.SetExe(_editing, value);
            OnPropertyChanged();
            UpdateActive();
        }
    }

    /// <summary>Programas que estiveram em foco (sugestões para associar a um perfil).</summary>
    public IReadOnlyCollection<string> RecentExes => _foreground.RecentExes;
    public void RefreshRecent() => OnPropertyChanged(nameof(RecentExes));

    public void New()
    {
        // Sugere o último programa em foco (provavelmente o jogo de onde você veio).
        string? exe = _foreground.RecentExes.FirstOrDefault(e => _store.Profiles.All(p => !string.Equals(p.Exe, e, StringComparison.OrdinalIgnoreCase)));
        string name = exe != null ? System.IO.Path.GetFileNameWithoutExtension(exe) : "Novo perfil";
        var p = _store.Create(name, exe);
        RefreshList();
        Editing = p;
        UpdateActive();
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
