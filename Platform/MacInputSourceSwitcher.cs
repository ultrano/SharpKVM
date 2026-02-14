using System;
using System.Runtime.InteropServices;

namespace SharpKVM;

public static class MacInputSourceSwitcher
{
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
            IntPtr keyDown = CGEventCreateKeyboardEvent(IntPtr.Zero, (ushort)hotkey.MacVirtualKeyCode, true);
            if (keyDown == IntPtr.Zero) return false;

            CGEventSetFlags(keyDown, hotkey.MacModifierFlags);
            CGEventPost(0, keyDown);
            CFRelease(keyDown);

            IntPtr keyUp = CGEventCreateKeyboardEvent(IntPtr.Zero, (ushort)hotkey.MacVirtualKeyCode, false);
            if (keyUp == IntPtr.Zero) return false;

            CGEventSetFlags(keyUp, hotkey.MacModifierFlags);
            CGEventPost(0, keyUp);
            CFRelease(keyUp);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
