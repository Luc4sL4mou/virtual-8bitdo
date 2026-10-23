using System.Globalization;
using System.Text.Json;

namespace Virtual8BitDo.Catalog;

/// <summary>Carrega o models.json e responde "que modelo é este VID/PID?".</summary>
public sealed class ModelCatalog
{
    private readonly Dictionary<(ushort, ushort), ControllerModel> _byVidPid = new();

    public IReadOnlyList<ControllerModel> Models { get; }
    public IReadOnlyList<ControllerModel> GenericProfiles { get; }

    /// <summary>Opções para a escolha manual: perfis genéricos primeiro, depois os modelos conhecidos.</summary>
    public IReadOnlyList<ControllerModel> ManualChoices { get; }

    /// <summary>Mensagem de erro se o arquivo não pôde ser lido (o catálogo fica vazio, o app continua funcionando).</summary>
    public string? LoadError { get; }

    private ModelCatalog(CatalogFile file, string? error)
    {
        Models = file.Models;
        GenericProfiles = file.GenericProfiles;
        ManualChoices = file.GenericProfiles.Concat(file.Models).ToList();
        LoadError = error;

        foreach (var m in file.Models)
        {
            if (TryParseHex(m.Vid, out var vid) && TryParseHex(m.Pid, out var pid))
                _byVidPid[(vid, pid)] = m;
        }
    }

    public static ModelCatalog Load(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var file = JsonSerializer.Deserialize<CatalogFile>(json, new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            }) ?? new CatalogFile();
            return new ModelCatalog(file, null);
        }
        catch (Exception ex)
        {
            return new ModelCatalog(new CatalogFile(), $"Não foi possível ler {path}: {ex.Message}");
        }
    }

    /// <summary>Retorna o modelo, ou null se o VID/PID não está no catálogo (modelo desconhecido).</summary>
    public ControllerModel? Find(ushort vid, ushort pid) =>
        _byVidPid.TryGetValue((vid, pid), out var m) ? m : null;

    public static ushort? ParseHex(string? text) => TryParseHex(text, out var v) ? v : null;

    private static bool TryParseHex(string? text, out ushort value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;
        var s = text.Trim();
        if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s[2..];
        return ushort.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }
}
