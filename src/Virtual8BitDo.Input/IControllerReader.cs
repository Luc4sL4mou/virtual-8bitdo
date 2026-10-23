namespace Virtual8BitDo.Input;

/// <summary>
/// Contrato da camada de leitura. A UI só conhece esta interface, então no futuro
/// podemos trocar o SDL por outra fonte (ou adicionar o controle virtual ViGEm) sem mexer na UI.
/// </summary>
public interface IControllerReader : IDisposable
{
    event Action<ControllerInfo>? ControllerConnected;
    event Action<int>? ControllerDisconnected;

    IReadOnlyList<ControllerInfo> Controllers { get; }

    /// <summary>Processa eventos (plug/unplug). Chame periodicamente, na mesma thread que criou o leitor.</summary>
    void Poll();

    ControllerSnapshot? GetSnapshot(int instanceId);

    /// <summary>Vibra o controle físico (motor de baixa e de alta frequência, 0..65535). Mesma thread do Poll.</summary>
    void Rumble(int instanceId, ushort lowFrequency, ushort highFrequency);
}
