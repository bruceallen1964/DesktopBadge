using System.Text.Json.Serialization;

namespace DesktopBadge.Core;

public enum BadgePosition
{
    TopLeft,
    TopCenter,
    TopRight,
    MiddleLeft,
    MiddleRight,
    BottomLeft,
    BottomCenter,
    BottomRight,
    Free
}

public enum BadgeOrientation
{
    Horizontal,
    Vertical
}

public enum BadgeSize
{
    Small,
    Medium,
    Large
}

/// <summary>Everything the badge remembers between runs.</summary>
public sealed class AppSettings
{
    public int Version { get; set; } = 1;

    public BadgePosition Position { get; set; } = BadgePosition.TopRight;

    public BadgeOrientation Orientation { get; set; } = BadgeOrientation.Horizontal;

    public BadgeSize Size { get; set; } = BadgeSize.Medium;

    /// <summary>Locked means click-through and not draggable.</summary>
    public bool Locked { get; set; } = true;

    /// <summary>Overall badge opacity, 0.35 - 1.0.</summary>
    public double Opacity { get; set; } = 0.94;

    /// <summary>Prefix the name with "3 ·".</summary>
    public bool ShowIndex { get; set; }

    /// <summary>Distance from the screen edge for the eight presets, in DIPs.</summary>
    public int EdgeMargin { get; set; } = 16;

    /// <summary>Device name of the monitor the badge lives on ("\\.\DISPLAY1").</summary>
    public string? Monitor { get; set; }

    /// <summary>Centre of a freely placed badge, normalised to the monitor work area.</summary>
    public double? FreeX { get; set; }
    public double? FreeY { get; set; }

    /// <summary>Desktop GUID -> index in the 16 colour palette.</summary>
    public Dictionary<string, int> DesktopColors { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool StartWithWindows { get; set; }

    public void Normalize()
    {
        if (!Enum.IsDefined(Position)) Position = BadgePosition.TopRight;
        if (!Enum.IsDefined(Orientation)) Orientation = BadgeOrientation.Horizontal;
        if (!Enum.IsDefined(Size)) Size = BadgeSize.Medium;

        Opacity = Math.Clamp(Opacity, 0.35, 1.0);
        EdgeMargin = Math.Clamp(EdgeMargin, 0, 400);

        if (FreeX is double x) FreeX = Math.Clamp(x, 0.0, 1.0);
        if (FreeY is double y) FreeY = Math.Clamp(y, 0.0, 1.0);

        DesktopColors ??= new(StringComparer.OrdinalIgnoreCase);
        foreach (var key in DesktopColors.Keys.ToList())
        {
            var value = DesktopColors[key];
            if (value < 0 || value >= DesktopPalette.Count)
                DesktopColors.Remove(key);
        }

        if (Position == BadgePosition.Free && (FreeX is null || FreeY is null))
            Position = BadgePosition.TopRight;

        if (Position != BadgePosition.Free)
        {
            FreeX = FreeX is double fx ? Math.Clamp(fx, 0, 1) : 0.5;
            FreeY = FreeY is double fy ? Math.Clamp(fy, 0, 1) : 0.5;
        }
    }
}
