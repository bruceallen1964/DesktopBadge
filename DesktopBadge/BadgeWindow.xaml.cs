using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using DesktopBadge.Core;
using Microsoft.Win32;
using Drawing = System.Drawing;
using WinForms = System.Windows.Forms;

namespace DesktopBadge;

public partial class BadgeWindow : Window
{
    readonly AppSettings _settings;
    readonly DispatcherTimer _reflowTimer;

    IntPtr _hwnd;
    DesktopInfo? _desktop;
    Color _color = Colors.SteelBlue;
    bool _dragging;
    int _dragCursorX;
    int _dragCursorY;
    int _dragLeft;
    int _dragTop;
    bool _layoutQueued;
    bool _layoutRunning;
    bool _firstLayoutDone;
    bool _shownOnce;

    /// <summary>Raised after the user drags the badge (position is already saved to settings).</summary>
    public event EventHandler? PositionChangedByUser;

    public BadgeWindow(AppSettings settings)
    {
        _settings = settings;
        InitializeComponent();

        _reflowTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _reflowTimer.Tick += (_, _) =>
        {
            _reflowTimer.Stop();
            ScheduleLayout();
        };

        Loaded += (_, _) => ApplySettings();
        ContentRendered += (_, _) => ScheduleLayout();
        SizeChanged += (_, _) =>
        {
            if (!_layoutRunning) ScheduleLayout();
        };
        DpiChanged += (_, _) => QueueReflow();
        Closed += OnClosed;

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
    }

    public void SetDesktop(DesktopInfo desktop, Color color)
    {
        var switched = _desktop is not null && _desktop.Id != desktop.Id;
        var textChanged = _desktop?.Name != desktop.Name ||
                          _desktop?.Number != desktop.Number ||
                          _desktop?.Id != desktop.Id;

        _desktop = desktop;
        _color = color;

        Label.Text = _settings.ShowIndex
            ? $"{desktop.Number}  \u00b7  {desktop.Name}"
            : desktop.Name;

        ApplyColours();

        if (switched && _shownOnce)
            FadeIn();

        _shownOnce = true;
        ScheduleLayout();
    }

    /// <summary>Applies size, orientation, opacity, click-through and drag affordances.</summary>
    public void ApplySettings()
    {
        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.SetClickThrough(_hwnd, _settings.Locked);
            // Reassert topmost after SetWindowLong calls (SetClickThrough modifies extended styles).
            NativeMethods.SetTopmost(_hwnd, true);
        }

        Cursor = _settings.Locked ? Cursors.Arrow : Cursors.SizeAll;
        DragOutline.Visibility = _settings.Locked ? Visibility.Collapsed : Visibility.Visible;

        ApplyTypography();
        ApplyColours();

        if (_firstLayoutDone)
            Opacity = _settings.Opacity;

        ScheduleLayout();
    }

    /// <summary>Selects "free" mode, keeps the current pixel position and unlocks the badge.</summary>
    public void EnterFreeMode()
    {
        var rect = NativeMethods.GetRect(_hwnd);
        if (_hwnd != IntPtr.Zero && rect.Width > 2 && rect.Left > -5000)
        {
            var screen = ResolveScreen(rect);
            CaptureFreePosition(rect, screen);
        }
        else
        {
            _settings.Position = BadgePosition.Free;
            _settings.FreeX ??= 0.5;
            _settings.FreeY ??= 0.5;
        }

        _settings.Locked = false;
        ApplySettings();
        PositionChangedByUser?.Invoke(this, EventArgs.Empty);
    }

    void ApplyTypography()
    {
        double font;
        Thickness padding;
        double radius;
        double maxWidth;

        switch (_settings.Size)
        {
            case BadgeSize.Small:
                font = 11.0;
                padding = new Thickness(10, 4, 10, 4);
                radius = 7;
                maxWidth = 240;
                break;
            case BadgeSize.Large:
                font = 14.5;
                padding = new Thickness(14, 7, 14, 7);
                radius = 10;
                maxWidth = 360;
                break;
            default:
                font = 12.5;
                padding = new Thickness(12, 5, 12, 5);
                radius = 8;
                maxWidth = 300;
                break;
        }

        Label.FontSize = font;
        Label.MaxWidth = maxWidth;
        Pill.Padding = padding;
        Pill.CornerRadius = new CornerRadius(radius);
        DragOutline.RadiusX = radius + 1;
        DragOutline.RadiusY = radius + 1;

        Rotation.Angle = _settings.Orientation == BadgeOrientation.Vertical ? 270 : 0;
    }

    void ApplyColours()
    {
        Pill.Background = new SolidColorBrush(_color);
        Label.Foreground = DesktopPalette.TextBrushFor(_color);

        var dark = DesktopPalette.PrefersDarkText(_color);
        Pill.BorderBrush = new SolidColorBrush(dark
            ? Color.FromArgb(0x38, 0, 0, 0)
            : Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));
    }

    void FadeIn()
    {
        var animation = new DoubleAnimation(0.35, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.Stop
        };
        Root.BeginAnimation(OpacityProperty, animation);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.MakeOverlay(_hwnd);

        // Explicitly set topmost z-order. WPF's Topmost="True" in XAML is not
        // always reliable when extended styles are modified via SetWindowLong,
        // because WS_EX_TOPMOST is stored separately from the extended style.
        NativeMethods.SetTopmost(_hwnd, true);

        // Invisible until the first successful placement avoids a flash in the corner.
        Opacity = 0;

        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        ApplySettings();
    }

    IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return new IntPtr(NativeMethods.MA_NOACTIVATE);
        }

        return IntPtr.Zero;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (_settings.Locked || _hwnd == IntPtr.Zero) return;

        var cursor = WinForms.Cursor.Position;
        var rect = NativeMethods.GetRect(_hwnd);

        _dragging = true;
        _dragCursorX = cursor.X;
        _dragCursorY = cursor.Y;
        _dragLeft = rect.Left;
        _dragTop = rect.Top;

        CaptureMouse();
        Cursor = Cursors.SizeAll;
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_dragging) return;

        var cursor = WinForms.Cursor.Position;
        NativeMethods.MoveTo(
            _hwnd,
            _dragLeft + (cursor.X - _dragCursorX),
            _dragTop + (cursor.Y - _dragCursorY));
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!_dragging) return;

        _dragging = false;
        ReleaseMouseCapture();
        Cursor = Cursors.SizeAll;

        var cursor = WinForms.Cursor.Position;
        var moved = Math.Abs(cursor.X - _dragCursorX) + Math.Abs(cursor.Y - _dragCursorY);
        if (moved < 3) return;

        var rect = NativeMethods.GetRect(_hwnd);
        var screen = ResolveScreen(rect);
        var work = screen.WorkingArea;

        // Snap fully back onto the monitor the user dropped on.
        var left = Math.Clamp(rect.Left, work.Left, Math.Max(work.Left, work.Right - rect.Width));
        var top = Math.Clamp(rect.Top, work.Top, Math.Max(work.Top, work.Bottom - rect.Height));
        NativeMethods.MoveTo(_hwnd, left, top);

        _settings.Locked = false;
        CaptureFreePosition(new NativeMethods.RECT
        {
            Left = left,
            Top = top,
            Right = left + rect.Width,
            Bottom = top + rect.Height
        }, screen);

        ApplySettings();
        PositionChangedByUser?.Invoke(this, EventArgs.Empty);
    }

    void CaptureFreePosition(NativeMethods.RECT rect, WinForms.Screen screen)
    {
        var work = screen.WorkingArea;
        _settings.Position = BadgePosition.Free;
        _settings.Monitor = screen.DeviceName;
        _settings.FreeX = Math.Clamp((rect.Left + rect.Width / 2.0 - work.Left) / work.Width, 0.0, 1.0);
        _settings.FreeY = Math.Clamp((rect.Top + rect.Height / 2.0 - work.Top) / work.Height, 0.0, 1.0);
    }

    void ScheduleLayout()
    {
        if (_layoutQueued || _hwnd == IntPtr.Zero) return;
        _layoutQueued = true;

        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _layoutQueued = false;
            LayoutNow();
        }));
    }

    void LayoutNow()
    {
        if (_hwnd == IntPtr.Zero || _dragging) return;

        // Periodically reassert topmost to fight Windows z-order quirks.
        NativeMethods.SetTopmost(_hwnd, true);

        _layoutRunning = true;
        try
        {
            UpdateLayout();
            var rect = NativeMethods.GetRect(_hwnd);
            if (rect.Width <= 2 || rect.Height <= 2)
            {
                QueueReflow();
                return;
            }

            var screen = ResolveScreen(rect);
            var scale = NativeMethods.GetScale(screen, _hwnd);
            var margin = (int)Math.Round(_settings.EdgeMargin * scale);
            var work = screen.WorkingArea;

            int x, y;
            if (_settings.Position == BadgePosition.Free &&
                _settings.FreeX is double nx &&
                _settings.FreeY is double ny)
            {
                var centreX = work.Left + nx * work.Width;
                var centreY = work.Top + ny * work.Height;
                x = (int)Math.Round(centreX - rect.Width / 2.0);
                y = (int)Math.Round(centreY - rect.Height / 2.0);
            }
            else
            {
                (x, y) = PresetPosition(_settings.Position, work, rect.Width, rect.Height, margin);
            }

            x = Math.Clamp(x, work.Left, Math.Max(work.Left, work.Right - rect.Width));
            y = Math.Clamp(y, work.Top, Math.Max(work.Top, work.Bottom - rect.Height));

            if (rect.Left != x || rect.Top != y)
                NativeMethods.MoveTo(_hwnd, x, y);

            if (!_firstLayoutDone)
            {
                _firstLayoutDone = true;
                Opacity = _settings.Opacity;
            }
        }
        finally
        {
            _layoutRunning = false;
        }
    }

    static (int X, int Y) PresetPosition(
        BadgePosition position, Drawing.Rectangle work, int width, int height, int margin)
    {
        var left = work.Left + margin;
        var right = work.Right - width - margin;
        var top = work.Top + margin;
        var bottom = work.Bottom - height - margin;
        var centreX = work.Left + (work.Width - width) / 2;
        var centreY = work.Top + (work.Height - height) / 2;

        return position switch
        {
            BadgePosition.TopLeft => (left, top),
            BadgePosition.TopCenter => (centreX, top),
            BadgePosition.TopRight => (right, top),
            BadgePosition.MiddleLeft => (left, centreY),
            BadgePosition.MiddleRight => (right, centreY),
            BadgePosition.BottomLeft => (left, bottom),
            BadgePosition.BottomCenter => (centreX, bottom),
            BadgePosition.BottomRight => (right, bottom),
            _ => (centreX, centreY)
        };
    }

    WinForms.Screen ResolveScreen(NativeMethods.RECT rect)
    {
        WinForms.Screen? screen = null;

        if (!string.IsNullOrWhiteSpace(_settings.Monitor))
        {
            screen = WinForms.Screen.AllScreens.FirstOrDefault(s =>
                string.Equals(s.DeviceName, _settings.Monitor, StringComparison.OrdinalIgnoreCase));
        }

        if (screen is null && rect.Width > 2)
        {
            var centre = new Drawing.Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            if (centre.X > -4000)
                screen = WinForms.Screen.FromPoint(centre);
        }

        return screen ?? WinForms.Screen.PrimaryScreen ?? WinForms.Screen.AllScreens[0];
    }

    void OnDisplaySettingsChanged(object? sender, EventArgs e)
    {
        if (Dispatcher.CheckAccess())
            QueueReflow();
        else
            Dispatcher.BeginInvoke(QueueReflow);
    }

    void QueueReflow()
    {
        _reflowTimer.Stop();
        _reflowTimer.Start();
    }

    void OnClosed(object? sender, EventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
    }
}
