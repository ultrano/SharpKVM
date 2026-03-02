using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace SharpKVM;

public readonly struct MacInputSourceSnapshot
{
    public bool IsAvailable { get; init; }
    public string Fingerprint { get; init; }
    public string Summary { get; init; }
    public string Detail { get; init; }

    public static MacInputSourceSnapshot Available(string fingerprint, string summary) =>
        new MacInputSourceSnapshot
        {
            IsAvailable = true,
            Fingerprint = fingerprint,
            Summary = summary,
            Detail = "ok"
        };

    public static MacInputSourceSnapshot Unavailable(string detail) =>
        new MacInputSourceSnapshot
        {
            IsAvailable = false,
            Fingerprint = string.Empty,
            Summary = "n/a",
            Detail = detail
        };

    public string ToLogValue()
    {
        if (!IsAvailable)
        {
            return $"unavailable({Detail})";
        }

        return Summary;
    }
}

public static class MacInputSourceStateProbe
{
    private static readonly Regex InputSourceIdRegex = new Regex("\"Input Source ID\"\\s*=\\s*\"([^\"]+)\";", RegexOptions.Compiled);
    private static readonly Regex InputModeRegex = new Regex("\"Input Mode\"\\s*=\\s*\"([^\"]+)\";", RegexOptions.Compiled);
    private static readonly Regex KeyboardLayoutNameRegex = new Regex("\"KeyboardLayout Name\"\\s*=\\s*\"([^\"]+)\";", RegexOptions.Compiled);
    private static readonly Regex BundleIdRegex = new Regex("\"Bundle ID\"\\s*=\\s*\"([^\"]+)\";", RegexOptions.Compiled);
    private const int ProcessTimeoutMs = 1500;

    public static MacInputSourceSnapshot Capture()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return MacInputSourceSnapshot.Unavailable("not_macos");
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "/usr/bin/defaults",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("read");
            startInfo.ArgumentList.Add("com.apple.HIToolbox");
            startInfo.ArgumentList.Add("AppleSelectedInputSources");

            using var process = Process.Start(startInfo);
            if (process == null)
            {
                return MacInputSourceSnapshot.Unavailable("process_start_failed");
            }

            if (!process.WaitForExit(ProcessTimeoutMs))
            {
                TryKill(process);
                return MacInputSourceSnapshot.Unavailable("defaults_timeout");
            }

            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            if (process.ExitCode != 0)
            {
                return MacInputSourceSnapshot.Unavailable($"defaults_failed:{process.ExitCode}:{Truncate(stderr, 120)}");
            }

            string fingerprint = NormalizeFingerprint(stdout);
            if (string.IsNullOrWhiteSpace(fingerprint))
            {
                return MacInputSourceSnapshot.Unavailable("empty_defaults_output");
            }

            string summary = ExtractSummary(stdout);
            return MacInputSourceSnapshot.Available(fingerprint, summary);
        }
        catch (Exception ex)
        {
            return MacInputSourceSnapshot.Unavailable($"exception:{ex.GetType().Name}");
        }
    }

    internal static string NormalizeFingerprint(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return string.Empty;
        }

        var lines = raw
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0);

        return string.Join(" ", lines);
    }

    internal static string ExtractSummary(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "unknown";
        }

        var summary = MatchFirstGroup(InputSourceIdRegex, raw)
            ?? MatchFirstGroup(InputModeRegex, raw)
            ?? MatchFirstGroup(KeyboardLayoutNameRegex, raw)
            ?? MatchFirstGroup(BundleIdRegex, raw);

        if (!string.IsNullOrWhiteSpace(summary))
        {
            return summary;
        }

        var fallback = raw
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line =>
                line != "(" &&
                line != ")" &&
                line != "{" &&
                line != "}" &&
                line != ");" &&
                line != "};");

        return string.IsNullOrWhiteSpace(fallback) ? "unknown" : Truncate(fallback, 120);
    }

    private static string? MatchFirstGroup(Regex regex, string text)
    {
        var match = regex.Match(text);
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        return match.Groups[1].Value.Trim();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
    }

    private static void TryKill(Process process)
    {
        try
        {
            process.Kill(true);
        }
        catch
        {
            // ignored
        }
    }
}
