namespace Virtual8BitDo.Input;

/// <summary>
/// IDs lógicos dos botões (iguais aos usados no catálogo models.json).
/// P1..P4 são os botões extras, na convenção do SDL:
///   P1 = direito superior, P2 = esquerdo superior, P3 = direito inferior, P4 = esquerdo inferior.
/// No 8BitDo Ultimate 2: P1 = R4, P2 = L4, P3 = PR, P4 = PL. O catálogo dá o nome de cada um.
/// </summary>
public static class StandardButtons
{
    public const string A = "A", B = "B", X = "X", Y = "Y";
    public const string LB = "LB", RB = "RB";
    public const string Back = "Back", Start = "Start", Guide = "Guide";
    public const string L3 = "L3", R3 = "R3";
    public const string DUp = "DUp", DDown = "DDown", DLeft = "DLeft", DRight = "DRight";
    public const string P1 = "P1", P2 = "P2", P3 = "P3", P4 = "P4";
}
