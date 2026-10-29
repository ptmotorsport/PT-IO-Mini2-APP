using IOMini2Tool.Models;
using IOMini2Tool.Services;
using Xunit;

namespace IOMini2Tool.Tests;

public sealed class DriverBindingTests
{
    [Fact]
    public void HealthySignedWinUsbBindingPasses()
    {
        var device = new DeviceInfo
        {
            DriverService = "WinUSB",
            DriverIsSigned = true,
            PnpStatus = "OK",
            ConfigManagerErrorCode = 0
        };

        Assert.True(DeviceDiscoveryService.TryValidateWinUsbBinding(device, out var error), error);
    }

    [Fact]
    public void HealthyWinUsbBindingIsNotRejectedByWmiSignatureFlag()
    {
        var device = new DeviceInfo
        {
            DriverService = "WinUSB",
            DriverIsSigned = false,
            PnpStatus = "OK",
            ConfigManagerErrorCode = 0
        };

        Assert.True(DeviceDiscoveryService.TryValidateWinUsbBinding(device, out var error), error);
    }

    [Theory]
    [InlineData("", "OK", 0)]
    [InlineData("libusbK", "OK", 0)]
    [InlineData("WinUSB", "Error", 0)]
    [InlineData("WinUSB", "OK", 28)]
    public void UnhealthyDriverBindingsFail(string service, string status, int configError)
    {
        var device = new DeviceInfo
        {
            DriverService = service,
            PnpStatus = status,
            ConfigManagerErrorCode = configError
        };

        Assert.False(DeviceDiscoveryService.TryValidateWinUsbBinding(device, out var error));
        Assert.NotEmpty(error);
    }
}
