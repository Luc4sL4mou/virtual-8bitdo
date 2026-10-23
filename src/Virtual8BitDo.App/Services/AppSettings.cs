using System.IO;
using System.Text.Json;

namespace Virtual8BitDo.App.Services;

/// <summary>Configurações do app, salvas em %AppData%\Virtual8BitDo\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Virtual8BitDo", "settings.json");

    /// <summary>"Não mostrar este aviso novamente" no aviso de anti-cheat.</summary>
    public bool HideAntiCheatWarning { get; set; }

    /// <summary>Repassar para o controle virtual (lembrado entre execuções).</summary>
    public bool Forwarding { get; set; } = true;

    public static AppSettings Load()
    {
        try
        {
            return File.Exists(FilePath)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath)) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception) { return new AppSettings(); }
    }

    public void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
    }
}
