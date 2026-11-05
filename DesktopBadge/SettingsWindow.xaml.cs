using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DesktopBadge.Core;

namespace DesktopBadge;

public partial class SettingsWindow : Window
{
    readonly AppSettings _settings;
    readonly Func<IReadOnlyList<DesktopInfo>> _getDesktops;
    readonly Func<Guid?> _getCurrent;
    readonly Action _apply;
    readonly Action<bool> _setAutoStart;
    readonly Action _enterFreeMode;

    readonly Dictionary<BadgePosition, ToggleButton> _positionButtons = new();
    readonly List<Border> _swatches = new();

    Guid? _selected;
    bool _syncing;
    bool _allowClose;

    public SettingsWindow(
        AppSettings settings,
        Func<IReadOnlyList<DesktopInfo>> getDesktops,
        Func<Guid?> getCurrent,
        Action apply,
        Action<bool> setAutoStart,
        Action enterFreeMode)
    {
        _settings = settings;
        _getDesktops = getDesktops;
        _getCurrent = getCurrent;
        _apply = apply;
        _setAutoStart = setAutoStart;
        _enterFreeMode = enterFreeMode;

        InitializeComponent();

        Height = Math.Min(860, SystemParameters.WorkArea.Height - 60);

        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = version is null ? string.Empty : $"v{version.ToString(2)}";

        BuildPositionButtons();
        BuildPalette();
        HookEvents();
        RefreshFromApp();
    }

    public void ShowAndActivate()
    {
        if (!IsVisible) Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        Activate();
        Topmost = true;
        Topmost = false;
        RefreshFromApp();
    }

    public void RefreshFromApp()
    {
        RefreshDesktopList();
        SyncFromSettings();
    }

    public void SyncFromSettings()
    {
        _syncing = true;
        try
        {
            foreach (var (position, button) in _positionButtons)
                button.IsChecked = _settings.Position == position;

            OrientationHorizontal.IsChecked = _settings.Orientation == BadgeOrientation.Horizontal;
            OrientationVertical.IsChecked = _settings.Orientation == BadgeOrientation.Vertical;

            SizeSmall.IsChecked = _settings.Size == BadgeSize.Small;
            SizeMedium.IsChecked = _settings.Size == BadgeSize.Medium;
            SizeLarge.IsChecked = _settings.Size == BadgeSize.Large;

            var percent = Math.Round(_settings.Opacity * 100);
            OpacitySlider.Value = percent;
            OpacityValue.Text = $"{percent:0}%";

            ShowIndexCheck.IsChecked = _settings.ShowIndex;
            LockCheck.IsChecked = _settings.Locked;
            StartupCheck.IsChecked = _settings.StartWithWindows;

            UpdatePreview();
        }
        finally
        {
            _syncing = false;
        }

        UpdatePaletteSelection();
    }

    public void CloseForReal()
    {
        _allowClose = true;
        Close();
    }

    void BuildPositionButtons()
    {
        var buttons = new[]
        {
            PosTopLeft, PosTopCenter, PosTopRight,
            PosMiddleLeft, PosFree, PosMiddleRight,
            PosBottomLeft, PosBottomCenter, PosBottomRight
        };

        foreach (var button in buttons)
        {
            if (button.Tag is string tag && Enum.TryParse<BadgePosition>(tag, out var position))
                _positionButtons[position] = button;
            button.Click += PositionClicked;
        }
    }

    void BuildPalette()
    {
        for (var i = 0; i < DesktopPalette.Count; i++)
        {
            var index = i;
            var swatch = new Border
            {
                Width = 38,
                Height = 26,
                Margin = new Thickness(3),
                CornerRadius = new CornerRadius(5),
                Background = DesktopPalette.BrushAt(i),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF)),
                Cursor = Cursors.Hand,
                ToolTip = $"{DesktopPalette.NameAt(i)}  ({DesktopPalette.Colors[i].Hex})",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            swatch.MouseLeftButtonUp += (_, e) =>
            {
                PickColour(index);
                e.Handled = true;
            };
            PaletteGrid.Children.Add(swatch);
            _swatches.Add(swatch);
        }
    }

    void HookEvents()
    {
        OrientationHorizontal.Click += (_, _) => SetOrientation(BadgeOrientation.Horizontal);
        OrientationVertical.Click += (_, _) => SetOrientation(BadgeOrientation.Vertical);

        SizeSmall.Click += (_, _) => SetSize(BadgeSize.Small);
        SizeMedium.Click += (_, _) => SetSize(BadgeSize.Medium);
        SizeLarge.Click += (_, _) => SetSize(BadgeSize.Large);

        OpacitySlider.ValueChanged += (_, e) =>
        {
            if (_syncing) return;
            _settings.Opacity = Math.Round(e.NewValue) / 100.0;
            OpacityValue.Text = $"{Math.Round(e.NewValue):0}%";
            _apply();
        };

        ShowIndexCheck.Checked += (_, _) => SetShowIndex(true);
        ShowIndexCheck.Unchecked += (_, _) => SetShowIndex(false);

        DesktopList.SelectionChanged += (_, _) =>
        {
            if (_syncing) return;
            _selected = (DesktopList.SelectedItem as DesktopRow)?.Id;
            UpdatePaletteSelection();
            UpdatePreview();
        };

        AutoColorButton.Click += (_, _) =>
        {
            if (_selected is not Guid id) return;
            _settings.DesktopColors.Remove(id.ToString("D"));
            _apply();
            RefreshFromApp();
        };

        LockCheck.Checked += (_, _) => SetLocked(true);
        LockCheck.Unchecked += (_, _) => SetLocked(false);

        StartupCheck.Checked += (_, _) => SetStartup(true);
        StartupCheck.Unchecked += (_, _) => SetStartup(false);

        CloseButton.Click += (_, _) => Hide();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Hide();
        };
    }

    void PositionClicked(object sender, RoutedEventArgs e)
    {
        if (_syncing || sender is not ToggleButton button || button.Tag is not string tag) return;
        if (!Enum.TryParse<BadgePosition>(tag, out var position)) return;

        if (position == BadgePosition.Free)
        {
            _enterFreeMode();
            SyncFromSettings();
            return;
        }

        _settings.Position = position;
        _apply();
        SyncFromSettings();
    }

    void SetOrientation(BadgeOrientation orientation)
    {
        if (_syncing) return;
        _settings.Orientation = orientation;
        _apply();
        SyncFromSettings();
    }

    void SetSize(BadgeSize size)
    {
        if (_syncing) return;
        _settings.Size = size;
        _apply();
        SyncFromSettings();
    }

    void SetShowIndex(bool value)
    {
        if (_syncing || _settings.ShowIndex == value) return;
        _settings.ShowIndex = value;
        _apply();
        UpdatePreview();
    }

    void SetLocked(bool value)
    {
        if (_syncing || _settings.Locked == value) return;
        _settings.Locked = value;
        _apply();
    }

    void SetStartup(bool value)
    {
        if (_syncing || _settings.StartWithWindows == value) return;
        _setAutoStart(value);
        SyncFromSettings();
    }

    void PickColour(int index)
    {
        if (_selected is not Guid id) return;
        _settings.DesktopColors[id.ToString("D")] = index;
        _apply();
        RefreshFromApp();
    }

    void RefreshDesktopList()
    {
        var desktops = _getDesktops();
        var current = _getCurrent();
        var previous = _selected;

        var rows = desktops.Select(d =>
        {
            var index = _settings.DesktopColors.TryGetValue(d.Id.ToString("D"), out var colour)
                ? colour
                : d.Number - 1;
            return new DesktopRow
            {
                Id = d.Id,
                Number = d.Number,
                Name = d.Name,
                Brush = DesktopPalette.BrushAt(index),
                IsCurrent = d.Id == current
            };
        }).ToList();

        _syncing = true;
        try
        {
            DesktopList.ItemsSource = rows;
            var pick = rows.FirstOrDefault(r => r.Id == previous)
                       ?? rows.FirstOrDefault(r => r.IsCurrent)
                       ?? rows.FirstOrDefault();
            DesktopList.SelectedItem = pick;
            _selected = pick?.Id;
        }
        finally
        {
            _syncing = false;
        }

        UpdatePaletteSelection();
        UpdatePreview();
    }

    void UpdatePaletteSelection()
    {
        var selectedIndex = -1;
        if (_selected is Guid id &&
            _settings.DesktopColors.TryGetValue(id.ToString("D"), out var colour))
        {
            selectedIndex = colour;
        }

        for (var i = 0; i < _swatches.Count; i++)
        {
            var isSelected = i == selectedIndex;
            _swatches[i].BorderThickness = new Thickness(isSelected ? 2 : 1);
            _swatches[i].BorderBrush = isSelected
                ? Brushes.White
                : new SolidColorBrush(Color.FromArgb(0x50, 0xFF, 0xFF, 0xFF));
        }

        ColorStatus.Text = selectedIndex >= 0
            ? $"{DesktopPalette.NameAt(selectedIndex)} is assigned to this desktop."
            : "Pick a colour for this desktop.";
    }

    void UpdatePreview()
    {
        var selected = _selected is Guid id ? _getDesktops().FirstOrDefault(d => d.Id == id) : null;
        selected ??= _getDesktops().FirstOrDefault(d => d.Id == _getCurrent());
        if (selected is null) return;

        var index = _settings.DesktopColors.TryGetValue(selected.Id.ToString("D"), out var colour)
            ? colour
            : 0;
        var accent = DesktopPalette.ColorAt(index);

        PreviewLabel.Text = _settings.ShowIndex
            ? $"{selected.Number}  \u00b7  {selected.Name}"
            : selected.Name;
        PreviewPill.Background = new SolidColorBrush(accent);
        PreviewLabel.Foreground = DesktopPalette.TextBrushFor(accent);
        PreviewPill.BorderBrush = new SolidColorBrush(
            DesktopPalette.PrefersDarkText(accent)
                ? Color.FromArgb(0x38, 0, 0, 0)
                : Color.FromArgb(0x48, 0xFF, 0xFF, 0xFF));

        double font;
        Thickness padding;
        double radius;
        switch (_settings.Size)
        {
            case BadgeSize.Small:
                font = 11.0;
                padding = new Thickness(10, 4, 10, 4);
                radius = 7;
                break;
            case BadgeSize.Large:
                font = 14.5;
                padding = new Thickness(14, 7, 14, 7);
                radius = 10;
                break;
            default:
                font = 12.5;
                padding = new Thickness(12, 5, 12, 5);
                radius = 8;
                break;
        }

        PreviewLabel.FontSize = font;
        PreviewPill.Padding = padding;
        PreviewPill.CornerRadius = new CornerRadius(radius);
        PreviewRotation.Angle = _settings.Orientation == BadgeOrientation.Vertical ? 270 : 0;
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        NativeMethods.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!_allowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    public sealed class DesktopRow
    {
        public Guid Id { get; init; }
        public int Number { get; init; }
        public string Name { get; init; } = string.Empty;
        public Brush Brush { get; init; } = Brushes.Gray;
        public bool IsCurrent { get; init; }
        public Visibility CurrentMark => IsCurrent ? Visibility.Visible : Visibility.Collapsed;
    }
}
