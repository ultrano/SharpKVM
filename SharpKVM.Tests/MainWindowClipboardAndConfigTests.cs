using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.ExceptionServices;

namespace SharpKVM.Tests;

public class MainWindowClipboardAndConfigTests
{
    [Fact]
    public void ExtractClipboardZipSafely_CorruptZip_LeavesExistingReceivedFiles()
    {
        using var temp = TempDirectory.Create();
        string receivedDir = Path.Combine(temp.Path, "ReceivedFiles");
        Directory.CreateDirectory(receivedDir);
        string existingFile = Path.Combine(receivedDir, "existing.txt");
        File.WriteAllText(existingFile, "keep");

        Assert.Throws<InvalidDataException>(() => InvokeExtractClipboardZipSafely(new byte[] { 1, 2, 3 }, receivedDir));

        Assert.True(File.Exists(existingFile));
        Assert.Equal("keep", File.ReadAllText(existingFile));
    }

    [Fact]
    public void ExtractClipboardZipSafely_PathTraversalZip_LeavesExistingReceivedFiles()
    {
        using var temp = TempDirectory.Create();
        string receivedDir = Path.Combine(temp.Path, "ReceivedFiles");
        Directory.CreateDirectory(receivedDir);
        string existingFile = Path.Combine(receivedDir, "existing.txt");
        string escapedFile = Path.Combine(temp.Path, "escape.txt");
        File.WriteAllText(existingFile, "keep");

        byte[] zip = CreateZip(("../escape.txt", "bad"));

        Assert.Throws<InvalidDataException>(() => InvokeExtractClipboardZipSafely(zip, receivedDir));

        Assert.True(File.Exists(existingFile));
        Assert.Equal("keep", File.ReadAllText(existingFile));
        Assert.False(File.Exists(escapedFile));
    }

    [Fact]
    public void ExtractClipboardZipSafely_ValidZip_ReplacesReceivedFilesAfterExtraction()
    {
        using var temp = TempDirectory.Create();
        string receivedDir = Path.Combine(temp.Path, "ReceivedFiles");
        Directory.CreateDirectory(receivedDir);
        string oldFile = Path.Combine(receivedDir, "old.txt");
        string newFile = Path.Combine(receivedDir, "folder", "new.txt");
        File.WriteAllText(oldFile, "old");

        byte[] zip = CreateZip(("folder/new.txt", "new"));

        var files = InvokeExtractClipboardZipSafely(zip, receivedDir);

        Assert.False(File.Exists(oldFile));
        Assert.True(File.Exists(newFile));
        Assert.Equal("new", File.ReadAllText(newFile));
        Assert.Contains(Path.GetFullPath(newFile), files);
    }

    [Fact]
    public void ClientConfigLine_RoundTripsNumericValuesWithInvariantCulture()
    {
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        CultureInfo originalUICulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fr-FR");
            var config = new ClientConfig
            {
                IP = "192.168.0.2",
                Sensitivity = 1.25,
                WheelSensitivity = 0.75,
                LayoutMode = LayoutMode.Free,
                X = 10.5,
                Y = 20.25,
                Width = 1920.5,
                Height = 1080.25,
                IsPlaced = true,
                IsSnapped = false,
                SnapAnchorID = "S1_Right",
                DesktopX = -1.5,
                DesktopY = 2.5,
                DesktopWidth = 2560.75,
                DesktopHeight = 1440.125
            };

            string line = InvokeFormatClientConfigLine(config);
            var parsed = InvokeParseClientConfigLine(line);

            Assert.Contains("1.25", line);
            Assert.DoesNotContain("1,25", line);
            Assert.Equal(config.IP, parsed.IP);
            Assert.Equal(config.Sensitivity, parsed.Sensitivity);
            Assert.Equal(config.WheelSensitivity, parsed.WheelSensitivity);
            Assert.Equal(config.LayoutMode, parsed.LayoutMode);
            Assert.Equal(config.X, parsed.X);
            Assert.Equal(config.Y, parsed.Y);
            Assert.Equal(config.Width, parsed.Width);
            Assert.Equal(config.Height, parsed.Height);
            Assert.Equal(config.IsPlaced, parsed.IsPlaced);
            Assert.Equal(config.IsSnapped, parsed.IsSnapped);
            Assert.Equal(config.SnapAnchorID, parsed.SnapAnchorID);
            Assert.Equal(config.DesktopX, parsed.DesktopX);
            Assert.Equal(config.DesktopY, parsed.DesktopY);
            Assert.Equal(config.DesktopWidth, parsed.DesktopWidth);
            Assert.Equal(config.DesktopHeight, parsed.DesktopHeight);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUICulture;
        }
    }

    private static List<string> InvokeExtractClipboardZipSafely(byte[] zipData, string saveDir)
    {
        return InvokePrivateStatic<List<string>>("ExtractClipboardZipSafely", zipData, saveDir);
    }

    private static string InvokeFormatClientConfigLine(ClientConfig config)
    {
        return InvokePrivateStatic<string>("FormatClientConfigLine", config);
    }

    private static ClientConfig InvokeParseClientConfigLine(string line)
    {
        var config = InvokePrivateStatic<ClientConfig?>("ParseClientConfigLine", line);
        Assert.NotNull(config);
        return config;
    }

    private static T InvokePrivateStatic<T>(string methodName, params object[] args)
    {
        var method = typeof(MainWindow).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        try
        {
            return (T)method!.Invoke(null, args)!;
        }
        catch (TargetInvocationException ex) when (ex.InnerException != null)
        {
            ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
            throw;
        }
    }

    private static byte[] CreateZip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return ms.ToArray();
    }

    private sealed class TempDirectory : IDisposable
    {
        private TempDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TempDirectory Create()
        {
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"SharpKVM.Tests.{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TempDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
    }
}
