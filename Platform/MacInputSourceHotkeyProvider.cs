using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;

namespace SharpKVM;

public static class MacInputSourceHotkeyProvider
{
    private const int SymbolicHotkeyPrimary = 60;
    private const int SymbolicHotkeySecondary = 61;

    private const string SymbolicHotkeysPlistRelativePath = "Library/Preferences/com.apple.symbolichotkeys.plist";
    private const string HitoolboxPlistRelativePath = "Library/Preferences/com.apple.HIToolbox.plist";

    private static readonly string[] KnownCapsLockOptionKeys =
    {
        "UseCapsLockSwitchToAndFromABC",
        "UseCapsLockToSwitchToAndFromABC",
        "AppleCapsLockSwitchToAndFromABC",
        "AppleGlobalTextInputProperties.UseCapsLockSwitchToAndFromABC",
        "AppleGlobalTextInputProperties.UseCapsLockToSwitchToAndFromABC",
        "AppleGlobalTextInputProperties.AppleCapsLockSwitchToAndFromABC"
    };

    public static bool TryLoad(out MacInputSourceHotkeys? hotkeys)
    {
        return TryLoadWithDiagnostics(out hotkeys, out _);
    }

    public static bool TryLoadWithDiagnostics(out MacInputSourceHotkeys? hotkeys, out MacInputSourceHotkeysDiagnostics diagnostics)
    {
        hotkeys = null;
        diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.NotMacOS, false, "unavailable", "n/a", "n/a", "n/a", "n/a", string.Empty);

        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.NotMacOS, false, "unavailable", "n/a", "n/a", "n/a", "n/a", "current platform is not macOS");
            return false;
        }

        string homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string symbolicPlistPath = Path.Combine(homePath, SymbolicHotkeysPlistRelativePath.Replace('/', Path.DirectorySeparatorChar));
        string hitoolboxPlistPath = Path.Combine(homePath, HitoolboxPlistRelativePath.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(symbolicPlistPath))
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.PlistNotFound, false, "unavailable", "n/a", "n/a", "n/a", "n/a", symbolicPlistPath);
            return false;
        }

        if (!TryReadPlistAsJson(symbolicPlistPath, out var symbolicJson, out var symbolicReadDetail))
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.PlutilFailed, false, "unavailable", "n/a", "n/a", "n/a", "n/a", symbolicReadDetail);
            return false;
        }

        try
        {
            using var symbolicDoc = JsonDocument.Parse(symbolicJson);
            if (!symbolicDoc.RootElement.TryGetProperty("AppleSymbolicHotKeys", out var hotkeysRoot))
            {
                diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.HotkeysRootMissing, false, "unavailable", "n/a", "n/a", "n/a", "n/a", "AppleSymbolicHotKeys missing");
                return false;
            }

            var primary = TryParseHotkey(hotkeysRoot, SymbolicHotkeyPrimary, "InputSourcePrimary");
            var secondary = TryParseHotkey(hotkeysRoot, SymbolicHotkeySecondary, "InputSourceSecondary");
            bool symbolicCapsLockEnabled = MacInputSourceHotkeys.ComputeCapsLockOptionEnabled(primary, secondary);

            var optionDetection = TryDetectCapsLockOptionFromHitoolboxPlist(hitoolboxPlistPath);
            bool hitoolboxCapsLockEnabled = optionDetection.HasValue && optionDetection.Value;
            bool finalCapsLockEnabled = hitoolboxCapsLockEnabled || symbolicCapsLockEnabled;

            hotkeys = new MacInputSourceHotkeys
            {
                Primary = primary,
                Secondary = secondary,
                IsCapsLockInputSourceSwitchEnabled = finalCapsLockEnabled
            };

            diagnostics = new MacInputSourceHotkeysDiagnostics
            {
                Status = (hotkeys.Primary != null || hotkeys.Secondary != null)
                    ? MacInputSourceHotkeysLoadStatus.Success
                    : MacInputSourceHotkeysLoadStatus.NoInputSourceHotkeys,
                IsCapsLockInputSourceSwitchEnabled = finalCapsLockEnabled,
                CapsLockOptionSource = ResolveOptionSource(optionDetection, symbolicCapsLockEnabled),
                RawOptionKey = optionDetection.RawKey,
                RawOptionValue = optionDetection.RawValue,
                PrimarySummary = DescribeHotkey(primary),
                SecondarySummary = DescribeHotkey(secondary),
                Details = $"symbolic={symbolicPlistPath}; hitoolbox={hitoolboxPlistPath}; detect={optionDetection.Detail}"
            };

            return diagnostics.Status == MacInputSourceHotkeysLoadStatus.Success;
        }
        catch
        {
            diagnostics = CreateDiagnostics(MacInputSourceHotkeysLoadStatus.JsonParseFailed, false, "unavailable", "n/a", "n/a", "n/a", "n/a", "json parse error");
            return false;
        }
    }

    private static string ResolveOptionSource(CapsLockOptionDetection detection, bool symbolicCapsLockEnabled)
    {
        if (detection.HasValue && symbolicCapsLockEnabled)
        {
            return $"{detection.Source}+symbolic";
        }

        if (detection.HasValue)
        {
            return detection.Source;
        }

        if (symbolicCapsLockEnabled)
        {
            return "symbolic_hotkeys";
        }

        return "unavailable";
    }

    private static CapsLockOptionDetection TryDetectCapsLockOptionFromHitoolboxPlist(string hitoolboxPlistPath)
    {
        if (!File.Exists(hitoolboxPlistPath))
        {
            return CapsLockOptionDetection.NotFound($"plist_missing:{hitoolboxPlistPath}");
        }

        if (!TryReadPlistAsJson(hitoolboxPlistPath, out var hitoolboxJson, out var readDetail))
        {
            return CapsLockOptionDetection.NotFound($"plutil_failed:{readDetail}");
        }

        return TryReadCapsLockOptionFromHitoolboxJson(hitoolboxJson);
    }

    private static bool TryReadPlistAsJson(string plistPath, out string json, out string detail)
    {
        json = string.Empty;
        detail = string.Empty;

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
                detail = $"plutil_start_failed:{plistPath}";
                return false;
            }

            json = p.StandardOutput.ReadToEnd();
            string err = p.StandardError.ReadToEnd();
            p.WaitForExit();
            if (p.ExitCode != 0 || string.IsNullOrWhiteSpace(json))
            {
                detail = $"plutil_exit={p.ExitCode},stderr={err}";
                return false;
            }

            detail = "ok";
            return true;
        }
        catch (Exception ex)
        {
            detail = $"plutil_exception:{ex.GetType().Name}";
            return false;
        }
    }

    internal static CapsLockOptionDetection TryReadCapsLockOptionFromHitoolboxJson(string hitoolboxJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(hitoolboxJson);

            foreach (var key in KnownCapsLockOptionKeys)
            {
                if (TryGetByPath(doc.RootElement, key, out var explicitMatch) &&
                    TryReadBooleanLike(explicitMatch, out var explicitValue, out var explicitRaw))
                {
                    return CapsLockOptionDetection.Found("hitoolbox_explicit", key, explicitRaw, explicitValue);
                }
            }

            if (TryFindCapsLockOptionRecursive(doc.RootElement, "$", out var discoveredPath, out var discoveredRaw, out var discoveredValue))
            {
                return CapsLockOptionDetection.Found("hitoolbox_recursive", discoveredPath, discoveredRaw, discoveredValue);
            }

            return CapsLockOptionDetection.NotFound("no_capslock_option_key");
        }
        catch (Exception ex)
        {
            return CapsLockOptionDetection.NotFound($"json_parse_error:{ex.GetType().Name}");
        }
    }

    private static bool TryGetByPath(JsonElement root, string dottedPath, out JsonElement value)
    {
        value = root;
        foreach (var segment in dottedPath.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty(segment, out var next))
            {
                return false;
            }
            value = next;
        }

        return true;
    }

    private static bool TryFindCapsLockOptionRecursive(
        JsonElement element,
        string currentPath,
        out string foundPath,
        out string rawValue,
        out bool value)
    {
        foundPath = "n/a";
        rawValue = "n/a";
        value = false;

        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                string propertyPath = currentPath == "$" ? property.Name : $"{currentPath}.{property.Name}";
                string normalizedName = property.Name.ToLowerInvariant();
                bool looksLikeCapsLockOption =
                    normalizedName.Contains("caps") &&
                    normalizedName.Contains("lock") &&
                    (normalizedName.Contains("switch") || normalizedName.Contains("input") || normalizedName.Contains("source") || normalizedName.Contains("abc"));

                if (looksLikeCapsLockOption &&
                    TryReadBooleanLike(property.Value, out var parsedValue, out var parsedRaw))
                {
                    foundPath = propertyPath;
                    rawValue = parsedRaw;
                    value = parsedValue;
                    return true;
                }

                if (TryFindCapsLockOptionRecursive(property.Value, propertyPath, out foundPath, out rawValue, out value))
                {
                    return true;
                }
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            int index = 0;
            foreach (var item in element.EnumerateArray())
            {
                string itemPath = $"{currentPath}[{index++}]";
                if (TryFindCapsLockOptionRecursive(item, itemPath, out foundPath, out rawValue, out value))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryReadBooleanLike(JsonElement element, out bool value, out string raw)
    {
        raw = element.ValueKind switch
        {
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString() ?? string.Empty,
            _ => element.GetRawText()
        };

        value = false;
        switch (element.ValueKind)
        {
            case JsonValueKind.True:
                value = true;
                return true;
            case JsonValueKind.False:
                value = false;
                return true;
            case JsonValueKind.Number:
                if (element.TryGetInt32(out var number))
                {
                    value = number != 0;
                    return true;
                }
                return false;
            case JsonValueKind.String:
                var text = (element.GetString() ?? string.Empty).Trim();
                if (string.Equals(text, "true", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "on", StringComparison.OrdinalIgnoreCase) ||
                    text == "1")
                {
                    value = true;
                    return true;
                }

                if (string.Equals(text, "false", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "no", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(text, "off", StringComparison.OrdinalIgnoreCase) ||
                    text == "0")
                {
                    value = false;
                    return true;
                }

                return false;
            default:
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
        string optionSource,
        string rawOptionKey,
        string rawOptionValue,
        string primarySummary,
        string secondarySummary,
        string details) =>
        new MacInputSourceHotkeysDiagnostics
        {
            Status = status,
            IsCapsLockInputSourceSwitchEnabled = capsLockEnabled,
            CapsLockOptionSource = optionSource,
            RawOptionKey = rawOptionKey,
            RawOptionValue = rawOptionValue,
            PrimarySummary = primarySummary,
            SecondarySummary = secondarySummary,
            Details = details
        };
}

internal readonly struct CapsLockOptionDetection
{
    public bool HasValue { get; }
    public bool Value { get; }
    public string Source { get; }
    public string RawKey { get; }
    public string RawValue { get; }
    public string Detail { get; }

    private CapsLockOptionDetection(bool hasValue, bool value, string source, string rawKey, string rawValue, string detail)
    {
        HasValue = hasValue;
        Value = value;
        Source = source;
        RawKey = rawKey;
        RawValue = rawValue;
        Detail = detail;
    }

    public static CapsLockOptionDetection Found(string source, string rawKey, string rawValue, bool value)
    {
        return new CapsLockOptionDetection(true, value, source, rawKey, rawValue, "found");
    }

    public static CapsLockOptionDetection NotFound(string detail)
    {
        return new CapsLockOptionDetection(false, false, "unavailable", "n/a", "n/a", detail);
    }
}
