using LocalDesktopStore.Services;
using Xunit;

namespace LocalDesktopStore.Tests;

public sealed class PublisherPinParserTests
{
    [Fact]
    public void NormalizesRepositoryAndCertificateSeparators()
    {
        var valid = PublisherPinParser.TryNormalize(
            " Acme/Example ",
            "0011:2233-4455 6677:8899 AABB-CCDD EEFF 0011-2233",
            out var repository,
            out var thumbprint,
            out var error);

        Assert.True(valid, error);
        Assert.Equal("Acme/Example", repository);
        Assert.Equal("00112233445566778899AABBCCDDEEFF00112233", thumbprint);
    }

    [Fact]
    public void RejectsMalformedPinLinesAndDuplicateEntries()
    {
        Assert.False(PublisherPinParser.TryParseLines(
            "Acme/Example=not-a-thumbprint", out _, out var invalidError));
        Assert.Contains("40-character", invalidError);

        Assert.False(PublisherPinParser.TryParseLines(
            "Acme/Example=00112233445566778899AABBCCDDEEFF00112233\nAcme/Example=00112233445566778899AABBCCDDEEFF00112233",
            out _, out var duplicateError));
        Assert.Contains("more than once", duplicateError);
    }

    [Fact]
    public void FormatsPinsInStableRepositoryOrder()
    {
        var formatted = PublisherPinParser.Format(new Dictionary<string, string>
        {
            ["Zed/App"] = "FFEEDDCCBBAA99887766554433221100FFEEDDCC",
            ["Acme/App"] = "00112233445566778899AABBCCDDEEFF00112233"
        });

        Assert.Equal(
            "Acme/App=00112233445566778899AABBCCDDEEFF00112233" + Environment.NewLine
            + "Zed/App=FFEEDDCCBBAA99887766554433221100FFEEDDCC",
            formatted);
    }
}
