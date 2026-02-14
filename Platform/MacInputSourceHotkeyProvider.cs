using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SharpKVM;

public static class MacInputSourceHotkeyProvider
{
    private const int SymbolicHotkeyPrimary = 60;
    private const int SymbolicHotkeySecondary = 61;

    public static bool TryLoad(out MacInputSourceHotkeys? hotkeys)
    {
        hotkeys = null;
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return false;
        }

        string plistPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Preferences",
            "com.apple.symbolichotkeys.plist");

        if (!File.Exists(plistPath))
        {
            return false;
        }

        string json;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/usr/bin/plutil",
                Arguments = $"-convert json -o - \"{plistPath}\"",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using var p = Process.Start(psi);
            if (p == null)
            {
                return false;
            }

            json = p.StandardOutput.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("AppleSymbolicHotKeys", out var hotkeysRoot))
            {
                return false;
            }

            var primary = TryParseHotkey(hotkeysRoot, SymbolicHotkeyPrimary, "InputSourcePrimary");
            var secondary = TryParseHotkey(hotkeysRoot, SymbolicHotkeySecondary, "InputSourceSecondary");

            hotkeys = new MacInputSourceHotkeys
            {
                Primary = primary,
                Secondary = secondary,
                IsCapsLockInputSourceSwitchEnabled =
                    (primary?.IsCapsLockPlainSwitch ?? false) ||
                    (secondary?.IsCapsLockPlainSwitch ?? false)
            };

            return hotkeys.Primary != null || hotkeys.Secondary != null;
        }
        catch
        {
            return false;
        }
    }

    private static MacInputSourceHotkey? TryParseHotkey(JsonElement root, int symbolicHotkeyId, string name)
    {
        if (!root.TryGetProperty(symbolicHotkeyId.ToString(), out var item))
        {
            return null;
        }

        if (!item.TryGetProperty("enabled", out var enabledEl) || enabledEl.GetInt32() != 1)
        {
            return null;
        }

        if (!item.TryGetProperty("value", out var valueEl) ||
            !valueEl.TryGetProperty("parameters", out var paramsEl) ||
            paramsEl.ValueKind != JsonValueKind.Array ||
            paramsEl.GetArrayLength() < 3)
        {
            return null;
        }

        int macKeyCode = paramsEl[1].GetInt32();
        ulong modifierFlags = paramsEl[2].GetUInt64();

        if (!MacInputSourceHotkeyMapper.TryMapMacVirtualKeyCode(macKeyCode, out var triggerKey))
        {
            return null;
        }

        return new MacInputSourceHotkey
        {
            Name = name,
            SymbolicHotkeyId = symbolicHotkeyId,
            MacVirtualKeyCode = macKeyCode,
            MacModifierFlags = modifierFlags,
            TriggerKey = triggerKey,
            RequiredModifiers = MacInputSourceHotkeyMapper.ToModifierMask(modifierFlags)
        };
    }
}
