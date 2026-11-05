using System.Windows.Media;

namespace DesktopBadge.Core;

public sealed record PaletteColor(string Name, string Hex)
{
    public Color Color { get; } = (Color)ColorConverter.ConvertFromString(Hex);
}

public static class DesktopPalette
{
    /// <summary>
    /// Sixteen identity colours. They are ordered so the first few desktops get
    /// strongly contrasting hues. Text colour is chosen per swatch for contrast.
    /// </summary>
    public static readonly IReadOnlyList<PaletteColor> Colors = new PaletteColor[]
    {
        new("Ocean",  "#4C8DFF"),
        new("Ember",  "#F0883E"),
        new("Fern",   "#3FB950"),
        new("Iris",   "#A371F7"),
        new("Lagoon", "#2BB3A3"),
        new("Cherry", "#E5534B"),
        new("Gold",   "#E3B341"),
        new("Indigo", "#6E7BF2"),
        new("Rose",   "#DB61A2"),
        new("Lime",   "#97C93D"),
        new("Sky",    "#33B1D6"),
        new("Orchid", "#C678DD"),
        new("Cocoa",  "#B08968"),
        new("Slate",  "#8B949E"),
        new("Navy",   "#3A5FA8"),
        new("Olive",  "#7A8B3A")
    };

    public static int Count => Colors.Count;

    static readonly SolidColorBrush[] Brushes = Colors
        .Select(c => { var b = new SolidColorBrush(c.Color); b.Freeze(); return b; })
        .ToArray();

    static readonly SolidColorBrush LightText = Frozen(Color.FromRgb(0xFF, 0xFF, 0xFF));
    static readonly SolidColorBrush DarkText = Frozen(Color.FromRgb(0x11, 0x14, 0x1A));

    public static string NameAt(int index) => Colors[Wrap(index)].Name;

    public static Color ColorAt(int index) => Colors[Wrap(index)].Color;

    public static SolidColorBrush BrushAt(int index) => Brushes[Wrap(index)];

    public static SolidColorBrush TextBrushFor(Color background) =>
        PrefersDarkText(background) ? DarkText : LightText;

    public static bool PrefersDarkText(Color background) => RelLuminance(background) > 0.28;

    static int Wrap(int index) => ((index % Count) + Count) % Count;

    static SolidColorBrush Frozen(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    static double RelLuminance(Color color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    static double Channel(byte value)
    {
        var s = value / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
