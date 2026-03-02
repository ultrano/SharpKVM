using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace SharpKVM;

public static class MacInputSourceSwitcher
{
    private const ushort CapsLockVirtualKeyCode = 57;
    private static string _lastError = "none";
    public static string LastError
    {
        get => Volatile.Read(ref _lastError);
        private set => Volatile.Write(ref _lastError, value);
    }

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr ev, ulong flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(uint tap, IntPtr ev);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetType(IntPtr ev, uint type);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CFRelease(IntPtr obj);

    private const uint kCGEventFlagsChanged = 12;
    private const ulong kCGEventFlagMaskAlphaShift = 0x10000;

    public static bool Execute(MacInputSourceHotkey hotkey)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            LastError = "not_macos";
            return false;
        }

        try
        {
            bool ok;
            if (hotkey.MacVirtualKeyCode == CapsLockVirtualKeyCode)
            {
                ok = ExecuteCapsLockFlagsChanged();
            }
            else
            {
                ok = ExecuteVirtualKey((ushort)hotkey.MacVirtualKeyCode, hotkey.MacModifierFlags);
            }
            LastError = ok
                ? $"ok:vkey={hotkey.MacVirtualKeyCode},flags=0x{hotkey.MacModifierFlags:X}"
                : $"execute_failed:vkey={hotkey.MacVirtualKeyCode},flags=0x{hotkey.MacModifierFlags:X}";
            return ok;
        }
        catch (Exception ex)
        {
            LastError = $"exception:{ex.GetType().Name}:{ex.Message}";
            return false;
        }
    }

    public static bool ExecuteCapsLockToggle()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            LastError = "not_macos";
            return false;
        }

        try
        {
            bool ok = ExecuteCapsLockFlagsChanged();
            LastError = ok ? "ok:capslock_toggle" : "execute_failed:capslock_toggle";
            return ok;
        }
        catch (Exception ex)
        {
            LastError = $"exception:{ex.GetType().Name}:{ex.Message}";
            return false;
        }
    }

    public static bool ExecuteControlSpaceFallback()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            LastError = "not_macos";
            return false;
        }

        try
        {
            // Prefer System Events key code path for fallback because some macOS builds
            // ignore synthetic CGEvent modifier combinations for input source switching.
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "osascript",
                    Arguments = "-e \"tell application \\\"System Events\\\" to key code 49 using control down\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            if (!process.Start())
            {
                LastError = "ctrl_space_script_start_failed";
                return false;
            }

            if (!process.WaitForExit(1500))
            {
                try { process.Kill(true); } catch { }
                LastError = "ctrl_space_script_timeout";
                return false;
            }

            string stderr = process.StandardError.ReadToEnd().Trim();
            if (process.ExitCode != 0)
            {
                LastError = $"ctrl_space_script_failed:{process.ExitCode}:{stderr}";
                return false;
            }

            LastError = "ok:ctrl_space_script";
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"ctrl_space_script_exception:{ex.GetType().Name}:{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// Simulate CapsLock as a kCGEventFlagsChanged event with AlphaShift flag,
    /// which is how macOS physically receives CapsLock and triggers input source switching.
    /// Plain CGEventCreateKeyboardEvent produces kCGEventKeyDown/KeyUp (type 10/11) which
    /// macOS does NOT recognize for input source (Korean/English) toggling.
    /// </summary>
    private static bool ExecuteCapsLockFlagsChanged()
    {
        IntPtr down = CGEventCreateKeyboardEvent(IntPtr.Zero, CapsLockVirtualKeyCode, true);
        if (down == IntPtr.Zero)
        {
            LastError = "cg_flagschanged_down_null";
            return false;
        }

        CGEventSetType(down, kCGEventFlagsChanged);
        CGEventSetFlags(down, kCGEventFlagMaskAlphaShift);
        CGEventPost(0, down);
        CFRelease(down);

        IntPtr up = CGEventCreateKeyboardEvent(IntPtr.Zero, CapsLockVirtualKeyCode, false);
        if (up == IntPtr.Zero)
        {
            LastError = "cg_flagschanged_up_null";
            return false;
        }

        CGEventSetType(up, kCGEventFlagsChanged);
        CGEventSetFlags(up, 0);
        CGEventPost(0, up);
        CFRelease(up);

        return true;
    }

    private static bool ExecuteVirtualKey(ushort virtualKeyCode, ulong modifierFlags)
    {
        IntPtr keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKeyCode, true);
        if (keyDown == IntPtr.Zero)
        {
            LastError = $"cg_keydown_null:vkey={virtualKeyCode}";
            return false;
        }

        CGEventSetFlags(keyDown, modifierFlags);
        CGEventPost(0, keyDown);
        CFRelease(keyDown);

        IntPtr keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKeyCode, false);
        if (keyUp == IntPtr.Zero)
        {
            LastError = $"cg_keyup_null:vkey={virtualKeyCode}";
            return false;
        }

        CGEventSetFlags(keyUp, modifierFlags);
        CGEventPost(0, keyUp);
        CFRelease(keyUp);
        return true;
    }
}
