using LocalDesktopStore.Services;
using Xunit;

namespace LocalDesktopStore.Tests;

public sealed class EnterpriseSettingsProtectorTests
{
    [Fact]
    public void MachineScopedDpapiRoundTripsWithoutStoringPlaintext()
    {
        const string token = "ghp-enterprise-test-token";

        var protectedValue = EnterpriseSettingsProtector.ProtectForMachine(token);

        Assert.NotEqual(token, protectedValue);
        Assert.Equal(token, EnterpriseSettingsProtector.UnprotectForMachine(protectedValue));
    }
}
