using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Virtual8BitDo.App.Views;

public enum ToastKind { Battery, Turbo }

/// <summary>
/// Pop-up flutuante de ~2 segundos. Se já houver um aberto do mesmo tipo, ele é atualizado e o tempo recomeça
/// (é o que permite ver o turbo mudando "em tempo real" enquanto você ajusta no controle).
/// </summary>
public partial class FloatingToast : Window
{
    private static readonly TimeSpan Visible = TimeSpan.FromSeconds(2);
    private static readonly Duration Fade = TimeSpan.FromMilliseconds(180);
    private static readonly Dictionary<ToastKind, FloatingToast> Open = new();

    private readonly ToastKind _kind;
    private readonly DispatcherTimer _hideTimer = new() { Interval = Visible };
    private bool _closing;

    private FloatingToast(ToastKind kind)
    {
        InitializeComponent();
        _kind = kind;
        BatteryIcon.Visibility = kind == ToastKind.Battery ? Visibility.Visible : Visibility.Collapsed;
        TurboIcon.Visibility = kind == ToastKind.Turbo ? Visibility.Visible : Visibility.Collapsed;
        Frame.BorderBrush = new SolidColorBrush(kind == ToastKind.Battery ? Color.FromRgb(0xC4, 0x2B, 0x1C) : Color.FromRgb(0x2E, 0x9C, 0xFF));

        _hideTimer.Tick += (_, _) => FadeOutAndClose();
        SourceInitialized += (_, _) => MakeToolWindow();
        SizeChanged += (_, _) => Place();
        Loaded += (_, _) => BeginAnimation(OpacityProperty, new DoubleAnimation(1, Fade));
        Closed += (_, _) => Open.Remove(_kind);
    }

    /// <summary>Mostra (ou atualiza) o pop-up. Funciona mesmo com a janela principal escondida na bandeja.</summary>
    public static void Show(ToastKind kind, string title, string subtitle)
    {
        if (!Open.TryGetValue(kind, out var toast))
        {
            Open[kind] = toast = new FloatingToast(kind);
            toast.SetText(title, subtitle);
            toast.Show();
        }
        else
        {
            toast.SetText(title, subtitle);
            toast.Revive();
        }
        toast._hideTimer.Stop();
        toast._hideTimer.Start();
    }

    /// <summary>Atualiza só o subtítulo de um pop-up aberto (sem prolongar o tempo dele).</summary>
    public static void UpdateSubtitle(ToastKind kind, string subtitle)
    {
        if (Open.TryGetValue(kind, out var toast) && !toast._closing) toast.SubtitleText.Text = subtitle;
    }

    public static bool IsOpen(ToastKind kind) => Open.ContainsKey(kind);

    private void SetText(string title, string subtitle)
    {
        TitleText.Text = title;
        SubtitleText.Text = subtitle;
    }

    /// <summary>Se estava sumindo, volta a aparecer por inteiro.</summary>
    private void Revive()
    {
        if (!_closing) return;
        _closing = false;
        BeginAnimation(OpacityProperty, new DoubleAnimation(1, Fade));
    }

    private void FadeOutAndClose()
    {
        _hideTimer.Stop();
        _closing = true;
        var fade = new DoubleAnimation(0, Fade);
        fade.Completed += (_, _) => { if (_closing) Close(); };
        BeginAnimation(OpacityProperty, fade);
    }

    /// <summary>Canto inferior direito da área de trabalho (acima da barra de tarefas).</summary>
    private void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - 8;
        Top = area.Bottom - ActualHeight - 8;
    }

    // Janela de ferramenta que não ativa: não aparece no Alt+Tab e não tira o foco do jogo.
    private const int GWL_EXSTYLE = -20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);

    private void MakeToolWindow()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
    }
}
