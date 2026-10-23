using System.Text.Json;

namespace Virtual8BitDo.Core.Profiles;

/// <summary>
/// Guarda os perfis como arquivos JSON (um por perfil) em %AppData%\Virtual8BitDo\profiles.
/// Sempre existe exatamente um perfil padrão, usado quando o jogo em foco não tem perfil próprio.
/// </summary>
public sealed class ProfileStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _folder;
    private readonly Dictionary<Profile, string> _files = new();
    private readonly List<Profile> _profiles = new();

    public ProfileStore(string? folder = null)
    {
        _folder = folder ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual8BitDo", "profiles");
        Directory.CreateDirectory(_folder);
        Load();
    }

    public string Folder => _folder;
    public IReadOnlyList<Profile> Profiles => _profiles;
    public Profile Default => _profiles.First(p => p.IsDefault);

    /// <summary>Perfil do jogo (pelo nome do .exe), ou o padrão se não houver.</summary>
    public Profile ForExe(string? exe) =>
        exe == null ? Default
        : _profiles.FirstOrDefault(p => !p.IsDefault && string.Equals(p.Exe, exe, StringComparison.OrdinalIgnoreCase)) ?? Default;

    private void Load()
    {
        foreach (var file in Directory.EnumerateFiles(_folder, "*.json"))
        {
            try
            {
                var p = JsonSerializer.Deserialize<Profile>(File.ReadAllText(file));
                if (p == null) continue;
                _profiles.Add(p);
                _files[p] = file;
            }
            catch (Exception) { /* arquivo corrompido: ignorado (continua na pasta para o usuário ver) */ }
        }

        // Exatamente um padrão: cria se faltar; se houver vários (edição manual), só o primeiro continua padrão.
        var defaults = _profiles.Where(p => p.IsDefault).ToList();
        if (defaults.Count == 0) Save(Add(new Profile { Name = "Padrão", IsDefault = true }));
        foreach (var extra in defaults.Skip(1)) { extra.IsDefault = false; Save(extra); }
        _profiles.Sort(Compare);
    }

    public Profile Create(string name, string? exe)
    {
        var p = Add(new Profile { Name = UniqueName(name), Exe = exe });
        Save(p);
        return p;
    }

    /// <summary>Cópia com outro nome. Não herda o jogo nem o papel de padrão (evita dois perfis para o mesmo .exe).</summary>
    public Profile Duplicate(Profile source)
    {
        var copy = source.Clone();
        copy.Name = UniqueName($"{source.Name} (cópia)");
        copy.Exe = null;
        copy.IsDefault = false;
        Add(copy);
        Save(copy);
        return copy;
    }

    public void Rename(Profile p, string newName)
    {
        newName = newName.Trim();
        if (newName.Length == 0 || newName == p.Name) return;
        p.Name = UniqueName(newName, except: p);
        if (_files.Remove(p, out var old) && File.Exists(old)) File.Delete(old);
        Save(p);
        _profiles.Sort(Compare);
    }

    /// <summary>Associa o perfil a um jogo (.exe). Se outro perfil já usava esse .exe, ele perde a associação.</summary>
    public void SetExe(Profile p, string? exe)
    {
        if (p.IsDefault) return;
        exe = string.IsNullOrWhiteSpace(exe) ? null : exe.Trim();
        if (exe != null)
            foreach (var other in _profiles.Where(o => o != p && string.Equals(o.Exe, exe, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                other.Exe = null;
                Save(other);
            }
        p.Exe = exe;
        Save(p);
    }

    public void Delete(Profile p)
    {
        if (p.IsDefault) throw new InvalidOperationException("O perfil padrão não pode ser apagado.");
        _profiles.Remove(p);
        if (_files.Remove(p, out var file) && File.Exists(file)) File.Delete(file);
    }

    public void Save(Profile p)
    {
        if (!_files.TryGetValue(p, out var file))
            _files[p] = file = Path.Combine(_folder, FileNameFor(p.Name));
        File.WriteAllText(file, JsonSerializer.Serialize(p, Json));
    }

    private Profile Add(Profile p)
    {
        _profiles.Add(p);
        _profiles.Sort(Compare);
        return p;
    }

    /// <summary>Padrão primeiro; depois em ordem alfabética.</summary>
    private static int Compare(Profile a, Profile b) =>
        a.IsDefault != b.IsDefault ? (a.IsDefault ? -1 : 1)
        : string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);

    private string UniqueName(string name, Profile? except = null)
    {
        string candidate = name;
        for (int i = 2; _profiles.Any(p => p != except && string.Equals(p.Name, candidate, StringComparison.CurrentCultureIgnoreCase)); i++)
            candidate = $"{name} {i}";
        return candidate;
    }

    private string FileNameFor(string name)
    {
        var safe = string.Concat(name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c));
        string file = safe + ".json";
        for (int i = 2; File.Exists(Path.Combine(_folder, file)); i++) file = $"{safe} {i}.json";
        return file;
    }
}
