using SharpHook.Native;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SharpKVM;

public sealed class RemotePressedKeyTracker
{
    private readonly Dictionary<string, HashSet<KeyCode>> _pressedByClient = new Dictionary<string, HashSet<KeyCode>>();

    public void TrackKeyDown(string clientKey, KeyCode code)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return;
        }

        if (!_pressedByClient.TryGetValue(clientKey, out var pressed))
        {
            pressed = new HashSet<KeyCode>();
            _pressedByClient[clientKey] = pressed;
        }

        pressed.Add(code);
    }

    public void TrackKeyUp(string clientKey, KeyCode code)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return;
        }

        if (!_pressedByClient.TryGetValue(clientKey, out var pressed))
        {
            return;
        }

        pressed.Remove(code);
        if (pressed.Count == 0)
        {
            _pressedByClient.Remove(clientKey);
        }
    }

    public IReadOnlyCollection<KeyCode> Drain(string clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return Array.Empty<KeyCode>();
        }

        if (!_pressedByClient.TryGetValue(clientKey, out var pressed) || pressed.Count == 0)
        {
            return Array.Empty<KeyCode>();
        }

        var drained = pressed.OrderBy(key => (int)key).ToArray();
        _pressedByClient.Remove(clientKey);
        return drained;
    }

    public int Count(string clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return 0;
        }

        return _pressedByClient.TryGetValue(clientKey, out var pressed) ? pressed.Count : 0;
    }

    public void Clear(string clientKey)
    {
        if (string.IsNullOrWhiteSpace(clientKey))
        {
            return;
        }

        _pressedByClient.Remove(clientKey);
    }

    public void ClearAll()
    {
        _pressedByClient.Clear();
    }
}
