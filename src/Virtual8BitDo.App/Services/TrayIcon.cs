using System.Windows;
using Forms = System.Windows.Forms;

namespace Virtual8BitDo.App.Services;

/// <summary>Ícone na bandeja do sistema: duplo clique abre a janela; menu com Abrir e Sair.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly Forms.NotifyIcon _icon;

    public TrayIcon(Action onOpen, Action onExit)
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Abrir Virtual 8BitDo", null, (_, _) => onOpen());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Sair", null, (_, _) => onExit());

        // O mesmo app.ico do executável, extraído do próprio .exe.
        var exeIcon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        _icon = new Forms.NotifyIcon
        {
            Icon = exeIcon,
            Text = "Virtual 8BitDo",
            Visible = true,
            ContextMenuStrip = menu,
        };
        _icon.DoubleClick += (_, _) => onOpen();
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
