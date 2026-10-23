namespace Virtual8BitDo.Input;

/// <summary>Como o controle está ligado ao PC.</summary>
public enum ConnectionType
{
    Unknown,
    /// <summary>Cabo USB direto no controle.</summary>
    Usb,
    /// <summary>Receptor/dongle sem fio de 2.4 GHz (não é Wi-Fi: é um rádio próprio da 8BitDo).</summary>
    Dongle24G,
    Bluetooth,
}

/// <summary>Estado de energia informado pelo controle.</summary>
public enum PowerState
{
    Unknown,
    /// <summary>Rodando na bateria (descarregando).</summary>
    OnBattery,
    /// <summary>Sem bateria (ex.: controle com fio).</summary>
    NoBattery,
    Charging,
    Charged,
}

/// <summary>
/// Bateria em um instante. <see cref="Percent"/> é null quando o modo de conexão não informa o nível.
/// <see cref="Approximate"/> = o controle só informa faixas (vazia/baixa/média/cheia), e o % é estimado.
/// </summary>
public readonly record struct BatteryStatus(PowerState State, int? Percent, bool Approximate = false)
{
    public static readonly BatteryStatus Unknown = new(PowerState.Unknown, null);
}
