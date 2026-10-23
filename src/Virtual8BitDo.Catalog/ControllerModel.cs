using System.Text.Json.Serialization;

namespace Virtual8BitDo.Catalog;

/// <summary>Botão extra (traseiro) que um modelo possui.</summary>
public sealed class ExtraButton
{
    /// <summary>Slot lógico: P1..P4 (mesmos IDs de StandardButtons na camada Input).</summary>
    [JsonPropertyName("id")] public string Id { get; set; } = "";

    /// <summary>Nome do botão neste modelo (PL, PR, L4, R4...).</summary>
    [JsonPropertyName("label")] public string Label { get; set; } = "";

    /// <summary>Índice do botão cru no Windows, quando o SDL não o mapeia como paddle. Opcional.</summary>
    [JsonPropertyName("rawIndex")] public int? RawIndex { get; set; }
}

/// <summary>Um modelo do catálogo (ou um perfil genérico).</summary>
public sealed class ControllerModel
{
    [JsonPropertyName("id")] public string? Id { get; set; }

    /// <summary>VID/PID em texto hexadecimal, ex.: "0x2DC8". Ausentes nos perfis genéricos.</summary>
    [JsonPropertyName("vid")] public string? Vid { get; set; }
    [JsonPropertyName("pid")] public string? Pid { get; set; }

    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("extraButtons")] public List<ExtraButton> ExtraButtons { get; set; } = new();

    /// <summary>Botões padrão que este modelo NÃO tem (ex.: "Guide").</summary>
    [JsonPropertyName("missingButtons")] public List<string> MissingButtons { get; set; } = new();

    /// <summary>False quando o controle tem os botões extras, mas neste modo de conexão eles não chegam ao PC.</summary>
    [JsonPropertyName("extrasReadable")] public bool ExtrasReadable { get; set; } = true;

    /// <summary>Modo de conexão que este VID/PID indica: "Usb", "Dongle24G" ou "Bluetooth". Usado quando o SDL não consegue saber.</summary>
    [JsonPropertyName("connection")] public string? Connection { get; set; }

    /// <summary>
    /// PID que o dongle usa quando está ocioso (ex.: "0x6013"). Quando cabo e dongle têm o mesmo PID,
    /// isso permite distinguir os dois pelo número de série USB. Opcional.
    /// </summary>
    [JsonPropertyName("receiverPid")] public string? ReceiverPid { get; set; }

    /// <summary>Aviso exibido na tela para este modelo/modo (opcional).</summary>
    [JsonPropertyName("note")] public string? Note { get; set; }

    public ushort? ReceiverPidValue => ModelCatalog.ParseHex(ReceiverPid);

    public override string ToString() => Name;
}

internal sealed class CatalogFile
{
    [JsonPropertyName("genericProfiles")] public List<ControllerModel> GenericProfiles { get; set; } = new();
    [JsonPropertyName("models")] public List<ControllerModel> Models { get; set; } = new();
}
