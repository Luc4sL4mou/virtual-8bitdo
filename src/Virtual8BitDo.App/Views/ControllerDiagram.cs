using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Virtual8BitDo.App.ViewModels;

namespace Virtual8BitDo.App.Views;

/// <summary>
/// Desenho do controle (vista de cima). Criado por código: cada botão é uma forma numa tabela,
/// e Render() pinta cada uma conforme o estado: normal, pressionado ou inacessível (cinza, tracejado).
/// </summary>
public sealed class ControllerDiagram : UserControl
{
    // Cores do tema (Themes/Colors.*.xaml), recarregadas quando o tema muda.
    private Color _normal, _pressed, _ring, _buttonText, _ringText, _onAccent;
    private Brush _disabledFill = Brushes.Gray, _disabledText = Brushes.Gray, _selectedStroke = Brushes.Pink, _customStroke = Brushes.Orange;
    private static readonly DoubleCollection Dashes = new() { 2, 2 };

    private enum Kind { Rect, Ellipse }

    private sealed record Spec(string Id, Kind Kind, double X, double Y, double W, double H, string Label, double Radius = 0);

    // Coordenadas num quadro de 600 x 380.
    private static readonly Spec[] Specs =
    {
        // Gatilhos e ombros ficam sobre os ombros do corpo (que agora é mais estreito).
        new("LT", Kind.Rect, 125, 14, 100, 38, "LT", 8),
        new("RT", Kind.Rect, 375, 14, 100, 38, "RT", 8),
        new("LB", Kind.Rect, 118, 58, 112, 24, "LB", 10),
        new("RB", Kind.Rect, 370, 58, 112, 24, "RB", 10),

        // Centro, como no 8BitDo Ultimate 2: Select (−) | Home | Start (+) na linha de cima.
        // Distribuição: cada grupo (analógico esquerdo, centro, A/B/X/Y, D-pad, analógico direito) tem folga em volta.
        new("Back",  Kind.Ellipse, 248, 112, 22, 22, "−"),
        new("Guide", Kind.Ellipse, 284, 107, 32, 32, "≡"),
        new("Start", Kind.Ellipse, 330, 112, 22, 22, "+"),

        new("Y", Kind.Ellipse, 417, 112, 30, 30, "Y"),
        new("A", Kind.Ellipse, 417, 178, 30, 30, "A"),
        new("X", Kind.Ellipse, 384, 145, 30, 30, "X"),
        new("B", Kind.Ellipse, 450, 145, 30, 30, "B"),

        new("DUp",    Kind.Rect, 215, 199, 26, 28, "", 4),
        new("DDown",  Kind.Rect, 215, 253, 26, 28, "", 4),
        new("DLeft",  Kind.Rect, 187, 227, 28, 26, "", 4),
        new("DRight", Kind.Rect, 241, 227, 28, 26, "", 4),

        new("L3", Kind.Ellipse, 138, 110, 76, 76, "L3"),
        new("R3", Kind.Ellipse, 314, 200, 76, 76, "R3"),

        // Extras superiores (L4/R4, do lado de dentro dos gatilhos) e traseiros (PL/PR, no lado de dentro das empunhaduras).
        // Convenção SDL: P1 = direito superior, P2 = esquerdo superior, P3 = direito inferior, P4 = esquerdo inferior.
        // L4/R4 ficam na frente do controle, encaixados do lado de dentro do LB/RB (na altura do ombro, colados na ponta do bumper).
        new("P2", Kind.Rect, 234, 55, 44, 26, "P2", 7),
        new("P1", Kind.Rect, 322, 55, 44, 26, "P1", 7),
        new("P4", Kind.Rect, 94, 304, 56, 26, "P4", 8),
        new("P3", Kind.Rect, 450, 304, 56, 26, "P3", 8),
    };

    private const double ThumbSize = 40;   // "cabeça" do analógico (o anel externo tem 76)
    private const double StickTravel = 16; // quanto a cabeça anda quando o stick vai até o limite

    private readonly Dictionary<string, (Shape Shape, TextBlock Label)> _parts = new();
    private readonly Ellipse _leftThumb = new();
    private readonly Ellipse _rightThumb = new();
    private readonly Path _body = BuildBody();
    private readonly List<Shape> _decorFills = new();
    private readonly List<UIElement> _decorMarks = new();
    private ControllerViewModel? _vm;
    private string? _selectedId;

    /// <summary>Um botão do desenho foi clicado (para escolher o que ele faz).</summary>
    public event Action<string>? ButtonClicked;

    /// <summary>Informa, para cada botão, se ele foi alterado no perfil e se tem turbo (contorno/rótulo âmbar).</summary>
    public Func<string, (bool Customized, bool Turbo)>? Decorate { get; set; }

    /// <summary>Botão selecionado no editor (contorno azul).</summary>
    public string? SelectedId
    {
        get => _selectedId;
        set { _selectedId = value; Render(); }
    }

    /// <summary>Redesenha (ex.: depois de mudar o mapeamento).</summary>
    public void Refresh() => Render();

    public ControllerDiagram()
    {
        var canvas = new Canvas { Width = 600, Height = 380 };

        canvas.Children.Add(_body);
        AddControllerOnlyButtons(canvas);

        foreach (var s in Specs)
        {
            Shape shape = s.Kind == Kind.Rect
                ? new Rectangle { RadiusX = s.Radius, RadiusY = s.Radius }
                : new Ellipse();
            shape.Width = s.W; shape.Height = s.H;
            Canvas.SetLeft(shape, s.X); Canvas.SetTop(shape, s.Y);
            shape.Cursor = System.Windows.Input.Cursors.Hand;
            string id = s.Id;
            shape.MouseLeftButtonUp += (_, _) => ButtonClicked?.Invoke(id);
            canvas.Children.Add(shape);

            var label = new TextBlock
            {
                Text = s.Label, FontSize = s.Id.StartsWith("L3") || s.Id.StartsWith("R3") ? 11 : 12,
                FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center,
                Width = s.W, IsHitTestVisible = false,
            };
            Canvas.SetLeft(label, s.X);
            // Os analógicos têm o texto na base para não ficar sob a "cabeça" do polegar.
            Canvas.SetTop(label, s.Y + (s.H - 17) / 2 + (s.Id is "L3" or "R3" ? 30 : 0));
            canvas.Children.Add(label);

            _parts[s.Id] = (shape, label);
        }

        // "Cabeças" dos analógicos: acompanham o movimento do stick.
        foreach (var (thumb, spec) in new[] { (_leftThumb, "L3"), (_rightThumb, "R3") })
        {
            var s = Specs.First(x => x.Id == spec);
            thumb.Width = thumb.Height = ThumbSize;
            thumb.IsHitTestVisible = false;
            Canvas.SetLeft(thumb, s.X + (s.W - ThumbSize) / 2);
            Canvas.SetTop(thumb, s.Y + (s.H - ThumbSize) / 2);
            thumb.RenderTransform = new TranslateTransform();
            canvas.Children.Add(thumb);
        }
        // Os rótulos L3/R3 ficam por cima das cabeças.
        foreach (var id in new[] { "L3", "R3" }) canvas.Children.Remove(_parts[id].Label);
        foreach (var id in new[] { "L3", "R3" }) canvas.Children.Add(_parts[id].Label);

        Content = new Viewbox { Child = canvas, StretchDirection = StretchDirection.DownOnly, MaxWidth = 600 };
        LoadThemeColors();
        Services.ThemeManager.Changed += LoadThemeColors;
    }

    private static Color ThemeColor(string key) =>
        Application.Current?.TryFindResource(key) is Color c ? c : Colors.Gray;

    /// <summary>Lê as cores do tema atual e repinta tudo (corpo, botões, enfeites).</summary>
    private void LoadThemeColors()
    {
        _normal = ThemeColor("DiagramButtonColor");
        _pressed = ThemeColor("DiagramPressedColor");
        _ring = ThemeColor("DiagramRingColor");
        _buttonText = ThemeColor("DiagramButtonTextColor");
        _ringText = ThemeColor("DiagramRingTextColor");
        _onAccent = (Application.Current?.TryFindResource("OnAccentBrush") as SolidColorBrush)?.Color ?? Colors.Black;
        _disabledFill = Freeze(new SolidColorBrush(ThemeColor("DiagramDisabledColor")));
        _disabledText = Freeze(new SolidColorBrush(ThemeColor("DiagramDisabledTextColor")));
        _selectedStroke = Freeze(new SolidColorBrush(ThemeColor("DiagramSelectedColor")));
        _customStroke = Freeze(new SolidColorBrush(ThemeColor("DiagramCustomColor")));

        _body.Fill = Freeze(new LinearGradientBrush(ThemeColor("DiagramBodyTopColor"), ThemeColor("DiagramBodyBottomColor"), 90));
        _body.Stroke = Freeze(new SolidColorBrush(ThemeColor("DiagramStrokeColor")));

        var decor = Freeze(new SolidColorBrush(ThemeColor("DiagramDecorColor")));
        var mark = Freeze(new SolidColorBrush(ThemeColor("DiagramDecorMarkColor")));
        foreach (var f in _decorFills) f.Fill = decor;
        foreach (var m in _decorMarks)
        {
            if (m is Shape { Fill: not null } filled) filled.Fill = mark;
            else if (m is Shape outline) outline.Stroke = mark;
            else if (m is TextBlock t) t.Foreground = mark;
        }
        Render();
    }

    /// <summary>
    /// Metade direita do contorno, no sentido horário a partir do centro do topo (quadro de 600 x 380).
    /// Cada trecho é uma curva de Bézier: (controle 1, controle 2, ponto final). A metade esquerda é o espelho.
    /// Formato do 8BitDo Ultimate 2: ombros arredondados, empunhaduras que descem e afinam, recorte côncavo embaixo.
    /// </summary>
    private static readonly Point BodyStart = new(300, 90);
    private static readonly (Point C1, Point C2, Point End)[] BodyRightHalf =
    {
        (new(350, 89),  new(395, 88),  new(425, 95)),    // topo, mais curto: o ombro começa mais para dentro
        (new(465, 102), new(500, 130), new(512, 180)),   // ombro arredondado descendo até a lateral
        (new(524, 225), new(538, 262), new(538, 300)),   // lateral externa da empunhadura
        (new(540, 340), new(525, 372), new(495, 372)),   // ponta arredondada da empunhadura
        (new(470, 376), new(440, 362), new(440, 340)),   // parte de baixo, voltando para dentro
        (new(438, 318), new(428, 302), new(410, 294)),   // lado interno da empunhadura, subindo
        (new(385, 298), new(345, 300), new(300, 300)),   // borda inferior central (recorte entre as empunhaduras)
    };

    /// <summary>O corpo do controle: uma peça só, com degradê suave e contorno fino.</summary>
    private static Path BuildBody()
    {
        static Point M(Point p) => new(600 - p.X, p.Y);

        var geometry = new StreamGeometry();
        using (var ctx = geometry.Open())
        {
            ctx.BeginFigure(BodyStart, isFilled: true, isClosed: true);
            foreach (var s in BodyRightHalf) ctx.BezierTo(s.C1, s.C2, s.End, isStroked: true, isSmoothJoin: true);

            // Metade esquerda: o mesmo caminho espelhado e percorrido ao contrário (controles trocam de ordem).
            for (int i = BodyRightHalf.Length - 1; i >= 0; i--)
            {
                var start = i == 0 ? BodyStart : BodyRightHalf[i - 1].End;
                ctx.BezierTo(M(BodyRightHalf[i].C2), M(BodyRightHalf[i].C1), M(start), isStroked: true, isSmoothJoin: true);
            }
        }
        geometry.Freeze();

        // As cores vêm do tema (LoadThemeColors); a sombra suave dá profundidade sobre o cartão.
        return new Path
        {
            Data = geometry,
            StrokeThickness = 1.5,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 28, ShadowDepth = 6, Direction = 270, Opacity = 0.35 },
            StrokeLineJoin = PenLineJoin.Round,
            IsHitTestVisible = false,
        };
    }

    /// <summary>
    /// Botões que só o próprio controle usa (não chegam ao PC): Screenshot, Estrela (turbo do controle)
    /// e o botão de perfil com as 3 luzes. Aparecem na arte, mas não são selecionáveis.
    /// </summary>
    private void AddControllerOnlyButtons(Canvas canvas)
    {
        const string tip = "Botão do próprio controle: não é enviado ao PC.";

        void Add(UIElement e, double x, double y)
        {
            if (e is FrameworkElement fe) fe.ToolTip = tip;
            if (e.IsHitTestVisible && e is Shape shape) _decorFills.Add(shape); else _decorMarks.Add(e);
            Canvas.SetLeft(e, x); Canvas.SetTop(e, y);
            canvas.Children.Add(e);
        }

        // Screenshot: quadrado arredondado com um círculo dentro.
        Add(new Rectangle { Width = 22, Height = 22, RadiusX = 5, RadiusY = 5 }, 266, 157);
        Add(new Ellipse { Width = 9, Height = 9, StrokeThickness = 1.5, IsHitTestVisible = false }, 272.5, 163.5);

        // Estrela: botão de turbo do controle.
        Add(new Ellipse { Width = 22, Height = 22 }, 312, 157);
        Add(new TextBlock { Text = "★", FontSize = 12, Width = 22, TextAlignment = TextAlignment.Center, IsHitTestVisible = false }, 312, 160);

        // Perfil do controle: botão em pílula com 3 luzes na vertical embaixo.
        Add(new Rectangle { Width = 30, Height = 10, RadiusX = 5, RadiusY = 5 }, 285, 196);
        for (int i = 0; i < 3; i++)
            Add(new Ellipse { Width = 4, Height = 4, Fill = Brushes.Gray, IsHitTestVisible = false }, 298, 212 + i * 9);
    }

    public void Bind(ControllerViewModel? vm)
    {
        if (_vm != null) _vm.Updated -= Render;
        _vm = vm;
        if (_vm != null) _vm.Updated += Render;
        Render();
    }

    private void Render()
    {
        foreach (var s in Specs)
        {
            var (shape, label) = _parts[s.Id];
            bool available = _vm == null || _vm.IsAvailable(s.Id);
            bool pressed = _vm?.PressedIds.Contains(s.Id) == true;

            // Gatilhos: a cor vai do normal ao azul conforme o quanto foi apertado.
            float analog = s.Id switch { "LT" => _vm?.LeftTrigger ?? 0, "RT" => _vm?.RightTrigger ?? 0, _ => pressed ? 1 : 0 };
            string text = s.Label;
            if (s.Id is "P1" or "P2" or "P3" or "P4") text = ButtonLabels.For(s.Id, _vm);
            if (s.Id is "LT" or "RT" && analog > 0.02f && available) text = $"{s.Label} {analog:P0}";
            label.Text = text;

            if (!available)
            {
                shape.Fill = _disabledFill;
                shape.Stroke = _disabledText;
                shape.StrokeThickness = 1;
                shape.StrokeDashArray = Dashes;
                label.Foreground = _disabledText;
            }
            else
            {
                shape.Fill = new SolidColorBrush(Blend(_normal, _pressed, analog));
                shape.Stroke = null;
                shape.StrokeDashArray = null;
                // L3/R3: o rótulo fica sobre o anel do analógico, não sobre um botão.
                label.Foreground = new SolidColorBrush(s.Id is "L3" or "R3" ? _ringText : analog > 0.5f ? _onAccent : _buttonText);
            }

            // Mapeamento do perfil em edição: âmbar = alterado / turbo; azul = selecionado no editor.
            var (customized, turbo) = Decorate?.Invoke(s.Id) ?? (false, false);
            if (turbo) label.Foreground = _customStroke;
            if (customized) { shape.Stroke = _customStroke; shape.StrokeThickness = 2; shape.StrokeDashArray = null; }
            if (s.Id == _selectedId) { shape.Stroke = _selectedStroke; shape.StrokeThickness = 3; shape.StrokeDashArray = null; }
        }

        RenderStick(_leftThumb, "L3", _vm?.LeftX ?? 0, _vm?.LeftY ?? 0);
        RenderStick(_rightThumb, "R3", _vm?.RightX ?? 0, _vm?.RightY ?? 0);
    }

    private void RenderStick(Ellipse thumb, string id, float x, float y)
    {
        bool pressed = _vm?.PressedIds.Contains(id) == true;
        bool available = _vm == null || _vm.IsAvailable(id);
        var (shape, _) = _parts[id];

        // O anel externo fica cinza claro; a cabeça é a parte que se move/pinta.
        if (available) shape.Fill = new SolidColorBrush(_ring);
        thumb.Fill = !available ? _disabledText : new SolidColorBrush(pressed ? _pressed : _normal);
        var t = (TranslateTransform)thumb.RenderTransform;
        t.X = x * StickTravel;
        t.Y = y * StickTravel;
    }

    private static Color Blend(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return Color.FromRgb(
            (byte)(a.R + (b.R - a.R) * t),
            (byte)(a.G + (b.G - a.G) * t),
            (byte)(a.B + (b.B - a.B) * t));
    }

    private static Brush Freeze(Brush b) { b.Freeze(); return b; }
}
