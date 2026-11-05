using System.Windows;
using System.Windows.Threading;
using DesktopBadge.Core;

namespace DesktopBadge;

public partial class App : Application
{
    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    Thread? _showThread;

    readonly SettingsStore _store = new();
    readonly VirtualDesktopService _desktops = new();

    AppSettings _settings = new();
    BadgeWindow? _badge;
    TrayIcon? _tray;
    SettingsWindow? _settingsWindow;
    DispatcherTimer? _pollTimer;
    DispatcherTimer? _saveTimer;
    bool _exiting;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A second launch should surface the settings of the running instance.
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, @"Local\DesktopBadge.ShowSettings");
        _mutex = new Mutex(true, @"Local\DesktopBadge.Instance", out var isFirstInstance);
        if (!isFirstInstance)
        {
            try { _showEvent.Set(); } catch { /* best effort */ }
            Shutdown();
            return;
        }

        _settings = _store.Load();
        _desktops.Refresh();
        EnsureColours();

        _settings.StartWithWindows = AutoStart.IsEnabled();
        if (_settings.StartWithWindows)
        {
            try { AutoStart.RefreshPath(); } catch (Exception ex) { _store.Log(ex); }
        }

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer?.Stop();
            _store.Save(_settings);
        };

        _badge = new BadgeWindow(_settings);
        _badge.PositionChangedByUser += (_, _) =>
        {
            _settingsWindow?.SyncFromSettings();
            ScheduleSave();
        };
        _badge.Show();

        _tray = new TrayIcon();
        _tray.OpenSettingsRequested += ShowSettings;
        _tray.ExitRequested += ExitApp;
        _tray.ToggleLockRequested += ToggleLock;
        _tray.ToggleStartupRequested += ToggleStartup;

        UpdateBadge();
        UpdateTray();

        StartShowThread();

        _pollTimer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(300),
            DispatcherPriority.Background,
            (_, _) => Poll(),
            Dispatcher);
        _pollTimer.Start();

        DispatcherUnhandledException += (_, args) =>
        {
            _store.Log(args.Exception);
            args.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            if (args.ExceptionObject is Exception ex) _store.Log(ex);
        };
        SessionEnding += (_, _) => _store.Save(_settings);

        ScheduleSave();
    }

    void Poll()
    {
        if (!_desktops.Refresh()) return;

        EnsureColours();
        UpdateBadge();
        UpdateTray();
        _settingsWindow?.RefreshFromApp();
    }

    void UpdateBadge()
    {
        var desktop = _desktops.Current;
        if (desktop is null || _badge is null) return;

        var index = _settings.DesktopColors.TryGetValue(desktop.Id.ToString("D"), out var colour)
            ? colour
            : 0;

        _badge.SetDesktop(desktop, DesktopPalette.ColorAt(index));
        _badge.ApplySettings();
    }

    void UpdateTray()
    {
        if (_tray is null) return;

        var desktop = _desktops.Current;
        var index = desktop is not null &&
                    _settings.DesktopColors.TryGetValue(desktop.Id.ToString("D"), out var colour)
            ? colour
            : 0;

        _tray.Update(
            desktop,
            DesktopPalette.ColorAt(index),
            _settings.Locked,
            _settings.StartWithWindows);
    }

    /// <summary>Gives every desktop a stable, unique palette slot.</summary>
    void EnsureColours()
    {
        var desktops = _desktops.Desktops;
        var ids = desktops.Select(d => d.Id).ToHashSet();
        var changed = false;

        foreach (var key in _settings.DesktopColors.Keys.ToList())
        {
            if (!Guid.TryParse(key, out var id) || !ids.Contains(id))
            {
                _settings.DesktopColors.Remove(key);
                changed = true;
            }
        }

        var used = new HashSet<int>();
        var missing = new List<DesktopInfo>();

        foreach (var desktop in desktops)
        {
            var key = desktop.Id.ToString("D");
            if (_settings.DesktopColors.TryGetValue(key, out var index) &&
                index >= 0 && index < DesktopPalette.Count &&
                used.Add(index))
            {
                continue;
            }

            missing.Add(desktop);
        }

        foreach (var desktop in missing)
        {
            var index = -1;
            for (var i = 0; i < DesktopPalette.Count; i++)
            {
                if (used.Contains(i)) continue;
                index = i;
                break;
            }

            if (index < 0)
                index = (desktop.Number - 1) % DesktopPalette.Count;

            _settings.DesktopColors[desktop.Id.ToString("D")] = index;
            used.Add(index);
            changed = true;
        }

        if (changed) ScheduleSave();
    }

    void ShowSettings()
    {
        if (_badge is null || _exiting) return;

        _settingsWindow ??= new SettingsWindow(
            _settings,
            () => _desktops.Desktops,
            () => _desktops.CurrentId,
            ApplyChanges,
            SetAutoStart,
            () => _badge.EnterFreeMode());

        _settingsWindow.ShowAndActivate();
    }

    void ApplyChanges()
    {
        EnsureColours();
        UpdateBadge();
        UpdateTray();
        _settingsWindow?.SyncFromSettings();
        ScheduleSave();
    }

    void ToggleLock()
    {
        _settings.Locked = !_settings.Locked;
        ApplyChanges();
    }

    void ToggleStartup() => SetAutoStart(!_settings.StartWithWindows);

    void SetAutoStart(bool enabled)
    {
        try
        {
            AutoStart.Set(enabled);
        }
        catch (Exception ex)
        {
            _store.Log(ex);
        }

        _settings.StartWithWindows = AutoStart.IsEnabled();
        UpdateTray();
        _settingsWindow?.SyncFromSettings();
        ScheduleSave();
    }

    void ScheduleSave()
    {
        if (_saveTimer is null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    void StartShowThread()
    {
        _showThread = new Thread(() =>
        {
            try
            {
                while (_showEvent is not null && _showEvent.WaitOne())
                {
                    if (_exiting) return;
                    Dispatcher.BeginInvoke(new Action(ShowSettings));
                }
            }
            catch
            {
                // The app is shutting down; nothing to do.
            }
        })
        {
            IsBackground = true,
            Name = "DesktopBadge instance listener"
        };
        _showThread.Start();
    }

    void ExitApp()
    {
        if (_exiting) return;
        _exiting = true;

        _pollTimer?.Stop();
        _saveTimer?.Stop();
        _store.Save(_settings);

        _settingsWindow?.CloseForReal();
        _tray?.Dispose();
        _badge?.Close();

        try { _mutex?.ReleaseMutex(); } catch { /* not owned */ }
        try { _showEvent?.Set(); } catch { /* shutting down */ }

        Shutdown();
    }
}
