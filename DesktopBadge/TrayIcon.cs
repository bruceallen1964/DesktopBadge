using System.Drawing;
using System.Windows.Forms;
using DesktopBadge.Core;

namespace DesktopBadge;

public sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon _notify;
    readonly ContextMenuStrip _menu;
    readonly ToolStripMenuItem _header;
    readonly ToolStripMenuItem _lockItem;
    readonly ToolStripMenuItem _startupItem;

    Icon? _icon;
    Guid _iconDesktop = Guid.Empty;
    System.Windows.Media.Color _iconColor;
    bool _lastLocked;
    bool _lastStartup;
    bool _initialised;

    public event Action? OpenSettingsRequested;
    public event Action? ExitRequested;
    public event Action? ToggleLockRequested;
    public event Action? ToggleStartupRequested;

    public TrayIcon()
    {
        _header = new ToolStripMenuItem("Desktop Badge") { Enabled = false };

        var settings = new ToolStripMenuItem("Settings\u2026");
        settings.Click += (_, _) => OpenSettingsRequested?.Invoke();

        _lockItem = new ToolStripMenuItem("Lock position");
        _lockItem.Click += (_, _) => ToggleLockRequested?.Invoke();

        _startupItem = new ToolStripMenuItem("Start with Windows");
        _startupItem.Click += (_, _) => ToggleStartupRequested?.Invoke();

        var exit = new ToolStripMenuItem("Exit");
        exit.Click += (_, _) => ExitRequested?.Invoke();

        _menu = new ContextMenuStrip
        {
            ShowImageMargin = false,
            Font = new Font("Segoe UI", 9f),
            Renderer = new ToolStripProfessionalRenderer()
        };
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _header,
            new ToolStripSeparator(),
            settings,
            new ToolStripSeparator(),
            _lockItem,
            _startupItem,
            new ToolStripSeparator(),
            exit
        });

        _notify = new NotifyIcon
        {
            Visible = true,
            ContextMenuStrip = _menu,
            Text = "Desktop Badge"
        };
        _notify.DoubleClick += (_, _) => OpenSettingsRequested?.Invoke();
        _notify.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left) OpenSettingsRequested?.Invoke();
        };
    }

    public void Update(DesktopInfo? desktop, System.Windows.Media.Color color, bool locked, bool startWithWindows)
    {
        if (desktop is not null)
        {
            _header.Text = $"{desktop.Name}   \u00b7   desktop {desktop.Number}";
            _notify.Text = Truncate($"{desktop.Name} \u2014 desktop {desktop.Number}", 63);
        }

        _lockItem.Checked = locked;
        _lockItem.Text = locked ? "Unlock position" : "Lock position";
        _startupItem.Checked = startWithWindows;

        if (!_initialised || locked != _lastLocked || startWithWindows != _lastStartup)
        {
            _menu.Refresh();
            _lastLocked = locked;
            _lastStartup = startWithWindows;
            _initialised = true;
        }

        if (desktop is not null && (desktop.Id != _iconDesktop || color != _iconColor || _icon is null))
        {
            var next = BadgeIconFactory.Create(color);
            _notify.Icon = next;
            _icon?.Dispose();
            _icon = next;
            _iconDesktop = desktop.Id;
            _iconColor = color;
        }
    }

    static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "\u2026";

    public void Dispose()
    {
        _notify.Visible = false;
        _notify.Icon = null;
        _notify.Dispose();
        _menu.Dispose();
        _icon?.Dispose();
    }
}
