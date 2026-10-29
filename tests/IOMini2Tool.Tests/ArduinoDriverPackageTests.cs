using IOMini2Tool.Services;
using Xunit;

namespace IOMini2Tool.Tests;

public sealed class ArduinoDriverPackageTests
{
    [Fact]
    public void AllowsOnlyKnownArduinoRenesasHardwareIds()
    {
        Assert.True(ArduinoRenesasDriverPackage.IsSupportedHardwareId(
            @"USB\VID_2341&PID_0369&REV_0100"));
        Assert.True(ArduinoRenesasDriverPackage.IsSupportedHardwareId(
            @"USB\VID_2341&PID_0069&REV_0100&MI_02"));
        Assert.False(ArduinoRenesasDriverPackage.IsSupportedHardwareId(
            @"USB\VID_2E8A&PID_000A&REV_0100"));
        Assert.False(ArduinoRenesasDriverPackage.IsSupportedHardwareId(
            @"USB\VID_2341&PID_9999&REV_0100"));
    }

    [Fact]
    public void RequiresValidCatalogSignatureFromArduino()
    {
        Assert.True(ArduinoRenesasDriverPackage.IsTrustedCatalogSignature(
            "Valid",
            "CN=Arduino SA, O=Arduino SA, S=Ticino, C=CH"));
        Assert.False(ArduinoRenesasDriverPackage.IsTrustedCatalogSignature(
            "Valid",
            "CN=Unrelated Vendor"));
        Assert.False(ArduinoRenesasDriverPackage.IsTrustedCatalogSignature(
            "NotSigned",
            "CN=Arduino SA"));
    }

    [Fact]
    public void AcceptsWinUsbInfOnlyWhenSelectedHardwareIdIsListed()
    {
        var inf = CreateInf(@"USB\VID_2341&PID_0369&REV_0100");

        var accepted = ArduinoRenesasDriverPackage.TryValidateInf(
            inf,
            [@"USB\VID_2341&PID_0369&REV_0100"],
            out var matchedId,
            out var error);

        Assert.True(accepted, error);
        Assert.Equal(@"USB\VID_2341&PID_0369&REV_0100", matchedId);
    }

    [Fact]
    public void RejectsCustomVidPidEvenWhenInfHasWinUsbSections()
    {
        var inf = CreateInf(@"USB\VID_2E8A&PID_000A&REV_0100");

        var accepted = ArduinoRenesasDriverPackage.TryValidateInf(
            inf,
            [@"USB\VID_2E8A&PID_000A&REV_0100"],
            out _,
            out var error);

        Assert.False(accepted);
        Assert.Contains("unsupported USB hardware ID", error);
    }

    [Fact]
    public void RejectsPackageWithoutExpectedArduinoIdentityOrWinUsbBinding()
    {
        var inf = CreateInf(@"USB\VID_2341&PID_0369&REV_0100")
            .Replace("ManufacturerName = \"Arduino\"", "ManufacturerName = \"Other Vendor\"", StringComparison.Ordinal);

        var accepted = ArduinoRenesasDriverPackage.TryValidateInf(
            inf,
            [@"USB\VID_2341&PID_0369&REV_0100"],
            out _,
            out var error);

        Assert.False(accepted);
        Assert.Contains("not the Arduino Renesas", error);
    }

    [Fact]
    public void RejectsSupportedIdMappedToDifferentInstallSection()
    {
        var inf = CreateInf(@"USB\VID_2341&PID_0369&REV_0100")
            .Replace("USB_Install, USB\\", "OtherInstall, USB\\", StringComparison.Ordinal);

        var accepted = ArduinoRenesasDriverPackage.TryValidateInf(
            inf,
            [@"USB\VID_2341&PID_0369&REV_0100"],
            out _,
            out var error);

        Assert.False(accepted);
        Assert.Contains("unexpected installation section", error);
    }

    private static string CreateInf(string hardwareId)
    {
        var suffix = hardwareId["USB\\".Length..];
        return $"""
            [Version]
            Provider = %ManufacturerName%
            Class = USBDevice
            CatalogFile = renesas.cat

            [Manufacturer]
            %ManufacturerName% = Standard,NTamd64

            [Standard.NTamd64]
            %DeviceName% = USB_Install, USB\%DeviceId%

            [USB_Install]
            Include = winusb.inf
            Needs = WINUSB.NT

            [USB_Install.Services]
            Include = winusb.inf
            Needs = WINUSB.NT.Services

            [Strings]
            ManufacturerName = "Arduino"
            DeviceName = "Test DFU"
            DeviceId = "{suffix}"
            """;
    }
}
