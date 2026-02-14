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
        return TryLoadWithDiagnostics(out hotkeys, out _);
    }

    public static bool TryLoadWithDiagnostics(out MacInputSourceHotkeys? hotkeys, out MacInputSourceHotkeysDiagnostics diagnostics)
    {
        hotkeys = null;
        diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.NotMacOS, false, "n/a", "n/a", string.Empty);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.NotMacOS, false, "n/a", "n/a", "current platform is not macOS");
            return false;
        }

        string plistPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library",
            "Preferences",
            "com.apple.symbolichotkeys.plist");

        if (!File.Exists(plistPath))
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.PlistNotFound, false, "n/a", "n/a", plistPath);
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
                diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.PlutilFailed, false, "n/a", "n/a", $"plutil exit={p.ExitCode}");
                return false;
            }
        }
        catch
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.PlutilFailed, false, "n/a", "n/a", "plutil execution error");
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("AppleSymbolicHotKeys", out var hotkeysRoot))
            {
                diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.HotkeysRootMissing, false, "n/a", "n/a", "AppleSymbolicHotKeys missing");
                return false;
            }

            var primary = TryParseHotkey(hotkeysRoot, SymbolicHotkeyPrimary, "InputSourcePrimary");
            var secondary = TryParseHotkey(hotkeysRoot, SymbolicHotkeySecondary, "InputSourceSecondary");
            bool capsLockEnabled = MacInputSourceHotkeys.ComputeCapsLockOptionEnabled(primary, secondary);

            hotkeys = new MacInputSourceHotkeys
            {
                Primary = primary,
                Secondary = secondary,
                IsCapsLockInputSourceSwitchEnabled = capsLockEnabled
            };

            diagnostics = new MacInputSourceHotkeysDiagnostics
            {
                Status = (hotkeys.Primary != null || hotkeys.Secondary != null)
                    ? MacInputSourceHotkeysLoadStatus.Success
                    : MacInputSourceHotkeysLoadStatus.NoInputSourceHotkeys,
                IsCapsLockInputSourceSwitchEnabled = capsLockEnabled,
                PrimarySummary = DescribeHotkey(primary),
                SecondarySummary = DescribeHotkey(secondary),
                Details = plistPath
            };

            return diagnostics.Status == MacInputSourceHotkeysLoadStatus.Success;
        }
        catch
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.JsonParseFailed, false, "n/a", "n/a", "json parse error");
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

    private static string DescribeHotkey(MacInputSourceHotkey? hotkey)
    {
        if (hotkey == null)
        {
            return "none";
        }

        return $"id={hotkey.SymbolicHotkeyId},key={hotkey.TriggerKey},mods={hotkey.RequiredModifiers},vkey={hotkey.MacVirtualKeyCode}";
    }

    private static MacInputSourceHotkeysDiagnostics CreateDiagnostics(
        MacInputSourceHotkeysLoadStatus status,
        bool capsLockEnabled,
        string primarySummary,
        string secondarySummary,
        string details) =>
        new MacInputSourceHotkeysDiagnostics
        {
            Status = status,
            IsCapsLockInputSourceSwitchEnabled = capsLockEnabled,
            PrimarySummary = primarySummary,
            SecondarySummary = secondarySummary,
            Details = details
        };
}
