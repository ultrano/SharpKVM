using System;
using System.Runtime.InteropServices;

namespace SharpKVM;

public static class MacAccessibilityDiagnostics
{
    [DllImport("/System/Library/Frameworks/ApplicationServices.framework/ApplicationServices")]
    private static extern bool AXIsProcessTrusted();

    public static bool IsAccessibilityTrusted()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        try
        {
            return AXIsProcessTrusted();
        }
        catch
        {
            return false;
        }
    }
}
