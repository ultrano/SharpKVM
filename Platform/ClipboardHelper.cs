using System;
using System.Diagnostics;
using System.IO;

namespace SharpKVM
{
    public static class ClipboardHelper
    {
        public static byte[]? GetWindowsClipboardImage()
        {
            try {
                string tempFile = Path.Combine(Path.GetTempPath(), "sharpkvm_clip.png");
                if (File.Exists(tempFile)) File.Delete(tempFile);
                var psCommand = "Add-Type -AssemblyName System.Windows.Forms; if ([System.Windows.Forms.Clipboard]::ContainsImage()) { $img = [System.Windows.Forms.Clipboard]::GetImage(); $img.Save('" + tempFile + "', [System.Drawing.Imaging.ImageFormat]::Png); $img.Dispose(); }";
                var info = new ProcessStartInfo("powershell", $"-Sta -Command \"{psCommand}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(info)?.WaitForExit();
                if (File.Exists(tempFile)) {
                    byte[] data = File.ReadAllBytes(tempFile);
                    File.Delete(tempFile);
                    return data;
                }
            } catch {}
            return null;
        }

        public static void SetMacClipboardImage(string imagePath)
        {
            try {
                var script = "set the clipboard to (read (POSIX file \"" + imagePath + "\") as {class PNGf})";
                var info = new ProcessStartInfo("osascript", $"-e '{script}'") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(info)?.WaitForExit();
            } catch {}
        }
        
        public static void SetWindowsClipboardImage(string imagePath)
        {
            try {
                var psCommand = $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.Clipboard]::SetImage([System.Drawing.Image]::FromFile('{imagePath}'))";
                var info = new ProcessStartInfo("powershell", $"-Sta -Command \"{psCommand}\"") { CreateNoWindow = true, UseShellExecute = false };
                Process.Start(info)?.WaitForExit();
            } catch {}
        }
    }

}
