using LocalDesktopStore.Services;
using Xunit;

namespace LocalDesktopStore.Tests;

public sealed class CommandLineParserTests
{
    [Fact]
    public async Task HostReturnsVersionWithoutConstructingTheSettingsServices()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CommandLineHost.RunAsync(["--version"], output, error);

        Assert.Equal(0, exitCode);
        Assert.StartsWith("LocalDesktopStore ", output.ToString(), StringComparison.Ordinal);
        Assert.Empty(error.ToString());
    }

    [Fact]
    public async Task HostReturnsArgumentErrorAndUsageForInvalidInput()
    {
        var output = new StringWriter();
        var error = new StringWriter();

        var exitCode = await CommandLineHost.RunAsync(["--install", "bad"], output, error);

        Assert.Equal(2, exitCode);
        Assert.Contains("OWNER/REPO", error.ToString());
        Assert.Contains("Usage:", error.ToString());
    }

    [Fact]
    public void ParsesInstallWithJsonAndInstallerArguments()
    {
        var parsed = CommandLineParser.TryParse(
            ["--install", "Acme/Example", "--json", "--installer-args", "INSTALLDIR=\"C:\\Apps\\Example\""],
            out var options,
            out var error);

        Assert.True(parsed, error);
        Assert.Equal(CliCommand.Install, options.Command);
        Assert.Equal("Acme/Example", options.Repository);
        Assert.True(options.Json);
        Assert.Equal("INSTALLDIR=\"C:\\Apps\\Example\"", options.InstallerArguments);
    }

    [Fact]
    public void RejectsMultipleCommandsAndMalformedRepositories()
    {
        Assert.False(CommandLineParser.TryParse(
            ["--list", "--refresh"], out _, out var multipleError));
        Assert.Contains("one CLI command", multipleError);

        Assert.False(CommandLineParser.TryParse(
            ["--install", "not-a-repository"], out _, out var repositoryError));
        Assert.Contains("OWNER/REPO", repositoryError);
    }

    [Fact]
    public void HeadlessWithoutCommandFallsBackToHelp()
    {
        Assert.True(CommandLineParser.TryParse(["--headless"], out var options, out var error), error);
        Assert.Equal(CliCommand.Help, options.Command);
    }

    [Fact]
    public void DetectsOnlyKnownCommandLineSwitches()
    {
        Assert.True(CommandLineParser.IsCommandLine(["--version"]));
        Assert.True(CommandLineParser.IsCommandLine(["--install=Acme/Example"]));
        Assert.False(CommandLineParser.IsCommandLine([]));
        Assert.False(CommandLineParser.IsCommandLine(["--unknown"]));
    }
}
