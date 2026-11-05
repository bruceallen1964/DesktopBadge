using Microsoft.Win32;

namespace DesktopBadge.Core;

public sealed record DesktopInfo(Guid Id, int Number, string Name, bool IsNamed);

/// <summary>
/// Reads the virtual-desktop list straight from Explorer's registry data.
///
/// HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops
///   VirtualDesktopIDs     REG_BINARY  ordered array of 16-byte GUIDs
///   CurrentVirtualDesktop REG_BINARY  the GUID of the desktop in use
///   Desktops\{guid}\Name  REG_SZ      the name the user gave the desktop
///
/// This is what Explorer itself uses, so names and the current desktop stay in
/// sync without the version-specific, undocumented COM interfaces.
/// </summary>
public sealed class VirtualDesktopService
{
    const string KeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

    DesktopInfo[] _desktops = Array.Empty<DesktopInfo>();
    Guid? _current;

    public IReadOnlyList<DesktopInfo> Desktops => _desktops;

    public Guid? CurrentId => _current;

    public DesktopInfo? Current => _desktops.FirstOrDefault(d => d.Id == _current);

    public DesktopInfo? ById(Guid id) => _desktops.FirstOrDefault(d => d.Id == id);

    /// <summary>Re-reads the registry. Returns true when anything visible changed.</summary>
    public bool Refresh()
    {
        try
        {
            using var root = Registry.CurrentUser.OpenSubKey(KeyPath, writable: false);
            if (root is null)
            {
                return SetState(Array.Empty<DesktopInfo>(), null);
            }

            var ids = ReadGuids(root.GetValue("VirtualDesktopIDs") as byte[]);
            var current = ReadGuid(root.GetValue("CurrentVirtualDesktop") as byte[]);

            if (ids.Count == 0)
            {
                // Extremely unlikely, but never leave the badge blank.
                ids.Add(Guid.Empty);
            }

            var list = new List<DesktopInfo>(ids.Count);
            for (var i = 0; i < ids.Count; i++)
            {
                var name = ReadName(root, ids[i]);
                var named = !string.IsNullOrWhiteSpace(name);
                list.Add(new DesktopInfo(
                    ids[i],
                    i + 1,
                    named ? name!.Trim() : $"Desktop {i + 1}",
                    named));
            }

            // If the current GUID momentarily does not match the list (mid-switch),
            // keep showing whatever we showed before instead of flickering.
            Guid resolved;
            if (current is Guid c && ids.Contains(c))
                resolved = c;
            else if (_current is Guid previous && ids.Contains(previous))
                resolved = previous;
            else
                resolved = ids[0];

            return SetState(list, resolved);
        }
        catch
        {
            // Registry access should never break the overlay.
            return false;
        }
    }

    bool SetState(IEnumerable<DesktopInfo> desktops, Guid? current)
    {
        var next = desktops.ToArray();
        var changed = !_desktops.SequenceEqual(next) || _current != current;
        _desktops = next;
        _current = current;
        return changed;
    }

    static string? ReadName(RegistryKey root, Guid id)
    {
        if (id == Guid.Empty) return null;
        try
        {
            using var key = root.OpenSubKey($@"Desktops\{{{id:D}}}", writable: false);
            return key?.GetValue("Name") as string;
        }
        catch
        {
            return null;
        }
    }

    static List<Guid> ReadGuids(byte[]? bytes)
    {
        var result = new List<Guid>();
        if (bytes is null || bytes.Length < 16) return result;

        for (var offset = 0; offset + 16 <= bytes.Length; offset += 16)
        {
            var slice = new byte[16];
            Buffer.BlockCopy(bytes, offset, slice, 0, 16);
            result.Add(new Guid(slice));
        }

        return result;
    }

    static Guid? ReadGuid(byte[]? bytes) =>
        bytes is { Length: >= 16 } ? new Guid(bytes.AsSpan(0, 16)) : null;
}
