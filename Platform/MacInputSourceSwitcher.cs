using System;
using System.Runtime.InteropServices;

namespace SharpKVM;

public static class MacInputSourceSwitcher
{
    private const ushort CapsLockVirtualKeyCode = 57;

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern IntPtr CGEventCreateKeyboardEvent(IntPtr source, ushort virtualKey, bool keyDown);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventSetFlags(IntPtr ev, ulong flags);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CGEventPost(uint tap, IntPtr ev);

    [DllImport("/System/Library/Frameworks/CoreGraphics.framework/CoreGraphics")]
    private static extern void CFRelease(IntPtr obj);

    public static bool Execute(MacInputSourceHotkey hotkey)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        try
        {
            return ExecuteVirtualKey((ushort)hotkey.MacVirtualKeyCode, hotkey.MacModifierFlags);
        }
        catch
        {
            return false;
        }
    }

    public static bool ExecuteCapsLockToggle()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        try
        {
            return ExecuteVirtualKey(CapsLockVirtualKeyCode, 0);
        }
        catch
        {
            return false;
        }
    }

    private static bool ExecuteVirtualKey(ushort virtualKeyCode, ulong modifierFlags)
    {
        IntPtr keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKeyCode, true);
        if (keyDown == IntPtr.Zero) return false;

        CGEventSetFlags(keyDown, modifierFlags);
        CGEventPost(0, keyDown);
        CFRelease(keyDown);

        IntPtr keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, virtualKeyCode, false);
        if (keyUp == IntPtr.Zero) return false;

        CGEventSetFlags(keyUp, modifierFlags);
        CGEventPost(0, keyUp);
        CFRelease(keyUp);
        return true;
    }
}
