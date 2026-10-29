using IOMini2Tool.Models;
using IOMini2Tool.Services;
using Xunit;

namespace IOMini2Tool.Tests;

public sealed class DfuPreflightTests
{
    [Fact]
    public void ParseListOutputExtractsUsbIdsSerialsAndPaths()
    {
        const string output =
            "Found DFU: [2341:0369] ver=0100, devnum=4, cfg=1, intf=0, path=\"1-2\", alt=0, name=\"Arduino DFU\", serial=\"SERIAL-1\"\r\n" +
            "Found DFU: [2341:0369] ver=0100, devnum=5, cfg=1, intf=0, path=\"1-3\", alt=0, name=\"Arduino DFU\", serial=\"SERIAL-2\"\r\n";

        var devices = DfuUtilService.ParseListOutput(output);

        Assert.Equal(2, devices.Count);
        Assert.Equal("2341:0369", devices[0].VidPid);
        Assert.Equal("SERIAL-1", devices[0].UsbSerial);
        Assert.Equal("1-2", devices[0].UsbPath);
        Assert.Equal("SERIAL-2", devices[1].UsbSerial);
        Assert.Equal(0, devices[0].AlternateSetting);
    }

    [Fact]
    public void MatchRequiresSelectedVidPidAndSerial()
    {
        var selected = new DeviceInfo { Id = "USB\\VID_2341&PID_0369\\SERIAL-2", VidPid = "2341:0369", UsbSerial = "SERIAL-2" };
        var listed = new[]
        {
            new DfuListDevice("2341:0369", "SERIAL-1", "1-2", "first"),
            new DfuListDevice("2341:0369", "SERIAL-2", "1-3", "second")
        };

        var matched = DfuUtilService.TryMatchSelectedDevice(selected, listed, out var result, out var error);

        Assert.True(matched, error);
        Assert.Equal("SERIAL-2", result?.UsbSerial);
    }

    [Fact]
    public void MatchBlocksAmbiguousDevicesWithoutSerial()
    {
        var selected = new DeviceInfo { Id = "USB\\VID_2341&PID_0369", VidPid = "2341:0369" };
        var listed = new[]
        {
            new DfuListDevice("2341:0369", null, "1-2", "first"),
            new DfuListDevice("2341:0369", null, "1-3", "second")
        };

        var matched = DfuUtilService.TryMatchSelectedDevice(selected, listed, out _, out var error);

        Assert.False(matched);
        Assert.Contains("More than one DFU device", error);
    }

    [Fact]
    public void MatchBlocksDevicesWithoutSafeTargetIdentifier()
    {
        var selected = new DeviceInfo { Id = "USB\\VID_2341&PID_0369", VidPid = "2341:0369" };
        var listed = new[] { new DfuListDevice("2341:0369", null, null, "device") };

        var matched = DfuUtilService.TryMatchSelectedDevice(selected, listed, out _, out var error);

        Assert.False(matched);
        Assert.Contains("cannot be identified safely", error);
    }

    [Fact]
    public void MatchCollapsesAlternateSettingsForOnePhysicalDevice()
    {
        var selected = new DeviceInfo { Id = "USB\\VID_2341&PID_0369\\SERIAL-1", VidPid = "2341:0369", UsbSerial = "SERIAL-1" };
        var listed = new[]
        {
            new DfuListDevice("2341:0369", "SERIAL-1", "1-2", "alt=1", 1),
            new DfuListDevice("2341:0369", "SERIAL-1", "1-2", "alt=0", 0)
        };

        var matched = DfuUtilService.TryMatchSelectedDevice(selected, listed, out var result, out var error);

        Assert.True(matched, error);
        Assert.Equal(0, result?.AlternateSetting);
    }

    [Fact]
    public void MatchBlocksReusedSerialOnDifferentUsbPaths()
    {
        var selected = new DeviceInfo { Id = "USB\\VID_2341&PID_0369\\SERIAL-1", VidPid = "2341:0369", UsbSerial = "SERIAL-1" };
        var listed = new[]
        {
            new DfuListDevice("2341:0369", "SERIAL-1", "1-2", "first", 0),
            new DfuListDevice("2341:0369", "SERIAL-1", "1-3", "second", 0)
        };

        var matched = DfuUtilService.TryMatchSelectedDevice(selected, listed, out _, out var error);

        Assert.False(matched);
        Assert.Contains("More than one DFU device", error);
    }
}
