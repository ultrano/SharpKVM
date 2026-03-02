using SharpKVM;

namespace SharpKVM.Tests;

public class MacInputSourceStateProbeTests
{
    [Fact]
    public void NormalizeFingerprint_TrimsWhitespaceAndCollapsesLines()
    {
        const string raw = """
                           (
                               {
                                   "Input Source ID" = "com.apple.keylayout.ABC";
                               }
                           )
                           """;

        var fingerprint = MacInputSourceStateProbe.NormalizeFingerprint(raw);

        Assert.Contains("\"Input Source ID\" = \"com.apple.keylayout.ABC\";", fingerprint);
        Assert.DoesNotContain("\n", fingerprint);
    }

    [Fact]
    public void ExtractSummary_PrefersInputSourceId()
    {
        const string raw = """
                           (
                               {
                                   "Input Source ID" = "com.apple.keylayout.ABC";
                                   "Input Mode" = "com.apple.inputmethod.Korean.2SetKorean";
                               }
                           )
                           """;

        var summary = MacInputSourceStateProbe.ExtractSummary(raw);

        Assert.Equal("com.apple.keylayout.ABC", summary);
    }

    [Fact]
    public void ExtractSummary_UsesInputModeWhenInputSourceIdMissing()
    {
        const string raw = """
                           (
                               {
                                   "Input Mode" = "com.apple.inputmethod.Korean.2SetKorean";
                                   "Bundle ID" = "com.apple.inputmethod.Korean";
                               }
                           )
                           """;

        var summary = MacInputSourceStateProbe.ExtractSummary(raw);

        Assert.Equal("com.apple.inputmethod.Korean.2SetKorean", summary);
    }

    [Fact]
    public void ExtractSummary_FallsBackToUnknownWhenNoSignalsExist()
    {
        const string raw = """
                           (
                               {
                               }
                           )
                           """;

        var summary = MacInputSourceStateProbe.ExtractSummary(raw);

        Assert.Equal("unknown", summary);
    }
}
