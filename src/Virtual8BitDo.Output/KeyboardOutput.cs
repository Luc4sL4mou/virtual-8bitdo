using System.Runtime.InteropServices;

namespace Virtual8BitDo.Output;

/// <summary>
/// Envia teclas para o jogo em foco, como se viessem de um teclado (SendInput com scan codes, que é o que a
/// maioria dos jogos lê). É assim que um botão extra (ex.: PL) vira uma tecla que a tela de atribuição do jogo detecta.
/// </summary>
public static class KeyboardOutput
{
    /// <summary>Teclas oferecidas na interface: nome exibido → código virtual do Windows.</summary>
    public static readonly IReadOnlyDictionary<string, ushort> Keys = BuildKeys();

    public static void Down(ushort vk) => Send(vk, up: false);
    public static void Up(ushort vk) => Send(vk, up: true);

    private static void Send(ushort vk, bool up)
    {
        uint scan = MapVirtualKeyW(vk, MAPVK_VK_TO_VSC_EX);
        uint flags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0);
        if ((scan & 0xFF00) == 0xE000) flags |= KEYEVENTF_EXTENDEDKEY; // setas, Insert, Home etc.

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            ki = new KEYBDINPUT { wVk = 0, wScan = (ushort)(scan & 0xFF), dwFlags = flags },
        };
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    private static Dictionary<string, ushort> BuildKeys()
    {
        var k = new Dictionary<string, ushort>();
        for (char c = 'A'; c <= 'Z'; c++) k[c.ToString()] = c;
        for (char c = '0'; c <= '9'; c++) k[c.ToString()] = c;
        for (int i = 1; i <= 24; i++) k[$"F{i}"] = (ushort)(0x70 + i - 1);
        k["Espaço"] = 0x20; k["Enter"] = 0x0D; k["Tab"] = 0x09; k["Esc"] = 0x1B; k["Backspace"] = 0x08;
        k["Shift esquerdo"] = 0xA0; k["Ctrl esquerdo"] = 0xA2; k["Alt esquerdo"] = 0xA4;
        k["Seta ↑"] = 0x26; k["Seta ↓"] = 0x28; k["Seta ←"] = 0x25; k["Seta →"] = 0x27;
        k["Insert"] = 0x2D; k["Delete"] = 0x2E; k["Home"] = 0x24; k["End"] = 0x23; k["Page Up"] = 0x21; k["Page Down"] = 0x22;
        for (int i = 0; i <= 9; i++) k[$"Num {i}"] = (ushort)(0x60 + i);
        return k;
    }

    // ---- Win32 ----
    private const int INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001, KEYEVENTF_KEYUP = 0x0002, KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC_EX = 4;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk; public ushort wScan; public uint dwFlags; public uint time; public IntPtr dwExtraInfo; }

    // O INPUT do Windows é uma união (teclado/mouse/hardware); o mouse é o maior membro, então reservamos o tamanho dele.
    [StructLayout(LayoutKind.Explicit, Size = 40)]
    private struct INPUT
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(8)] public KEYBDINPUT ki;
    }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern uint MapVirtualKeyW(uint code, uint mapType);
}
