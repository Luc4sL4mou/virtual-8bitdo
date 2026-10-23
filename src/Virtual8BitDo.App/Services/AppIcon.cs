using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Virtual8BitDo.App.Services;

/// <summary>
/// O ícone do app na maior resolução do .ico (256 px). Usar o .ico direto num Image pega o primeiro
/// tamanho (16 px) e estica, e o ícone fica borrado; reduzir a partir do maior fica nítido.
/// </summary>
public static class AppIcon
{
    public static ImageSource Large { get; } = Load();

    private static ImageSource Load()
    {
        var decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Assets/app.ico"),
                                           BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var frame = decoder.Frames.OrderByDescending(f => f.PixelWidth).First();
        frame.Freeze();
        return frame;
    }
}
