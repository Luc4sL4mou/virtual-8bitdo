using System.Text.Json;
using System.Text.Json.Serialization;
using Virtual8BitDo.Input;

namespace Virtual8BitDo.Core.Profiles;

public enum ActionKind
{
    /// <summary>O botão faz o que já fazia (o mesmo botão no Xbox virtual). Para os extras, equivale a "Nenhuma".</summary>
    Default,
    /// <summary>O botão não faz nada.</summary>
    None,
    /// <summary>Aperta um ou mais botões do Xbox virtual (ex.: "A", ou "LB"+"A" juntos).</summary>
    Xbox,
    /// <summary>Aperta uma tecla do teclado (que a tela de atribuição do jogo consegue detectar).</summary>
    Key,
}

/// <summary>O que um botão do controle físico faz.</summary>
public sealed class ButtonAction
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ActionKind Kind { get; set; } = ActionKind.Default;

    /// <summary>Alvos no Xbox virtual (IDs iguais aos de origem: "A", "LB", "LT", "DUp"...). Usado quando Kind = Xbox.</summary>
    public List<string> Xbox { get; set; } = new();

    /// <summary>Nome da tecla (ver KeyboardOutput.Keys). Usado quando Kind = Key.</summary>
    public string? Key { get; set; }

    /// <summary>Turbo: enquanto segurado, o botão "pisca" na frequência do perfil.</summary>
    public bool Turbo { get; set; }

    public ButtonAction Clone() => new() { Kind = Kind, Xbox = new List<string>(Xbox), Key = Key, Turbo = Turbo };
}

/// <summary>
/// Um perfil: as regras de mapeamento e turbo. Um por jogo (identificado pelo .exe) e um padrão (Exe = null).
/// Salvo como JSON em %AppData%\Virtual8BitDo\profiles.
/// </summary>
public sealed class Profile
{
    public const int MinTurboHz = 1, MaxTurboHz = 30;

    /// <summary>Botões de origem que podem ser configurados (o controle físico).</summary>
    public static readonly IReadOnlyList<string> Sources = new[]
    {
        StandardButtons.A, StandardButtons.B, StandardButtons.X, StandardButtons.Y,
        StandardButtons.LB, StandardButtons.RB, "LT", "RT",
        StandardButtons.Back, StandardButtons.Start, StandardButtons.Guide,
        StandardButtons.L3, StandardButtons.R3,
        StandardButtons.DUp, StandardButtons.DDown, StandardButtons.DLeft, StandardButtons.DRight,
        StandardButtons.P1, StandardButtons.P2, StandardButtons.P3, StandardButtons.P4,
    };

    /// <summary>Botões do Xbox 360 que podem ser alvo de um mapeamento.</summary>
    public static readonly IReadOnlyList<string> XboxTargets = Sources.Where(s => !IsExtra(s)).ToList();

    public string Name { get; set; } = "Padrão";

    /// <summary>O perfil usado quando o jogo em foco não tem perfil próprio. Existe exatamente um.</summary>
    public bool IsDefault { get; set; }

    /// <summary>Nome do executável do jogo (ex.: "eldenring.exe"). Null = sem jogo associado.</summary>
    public string? Exe { get; set; }

    public int TurboHz { get; set; } = 10;

    /// <summary>Só os botões alterados ficam aqui; os ausentes usam o comportamento padrão.</summary>
    public Dictionary<string, ButtonAction> Buttons { get; set; } = new();

    public static bool IsExtra(string id) => id is StandardButtons.P1 or StandardButtons.P2 or StandardButtons.P3 or StandardButtons.P4;

    /// <summary>Ação de um botão (a configurada, ou a padrão).</summary>
    public ButtonAction Get(string id) => Buttons.TryGetValue(id, out var a) ? a : new ButtonAction();

    /// <summary>Altera a ação de um botão; se voltar a ser a padrão sem turbo, some do arquivo.</summary>
    public void Set(string id, ButtonAction action)
    {
        if (action.Kind == ActionKind.Default && !action.Turbo) Buttons.Remove(id);
        else Buttons[id] = action;
    }

    public Profile Clone() => JsonSerializer.Deserialize<Profile>(JsonSerializer.Serialize(this))!;
}
