using AwesomeAssertions;
using ServiceLib.Geksa;
using Xunit;

namespace ServiceLib.Tests.Geksa;

public class GeksaEcpDeviceViewModelTests
{
    [Fact]
    public void AvailableDevice_ShouldHaveCorrectStateAndActionText()
    {
        var device = new GeksaVirtualUsbDevice(
            Address: "11",
            DisplayName: "Rutoken ECP",
            Vendor: "Rutoken",
            Product: "ECP",
            ServerName: "Office USB",
            ServerAddress: "192.168.1.10:7575",
            SerialNumber: "123456",
            State: GeksaVirtualUsbState.Available,
            InUseBy: string.Empty,
            AutoUseMode: "not-set",
            IsStorageDevice: false);

        var vm = new GeksaEcpDeviceViewModel(device, (_, _) => Task.CompletedTask);

        vm.IsAvailable.Should().BeTrue();
        vm.IsInUseLocally.Should().BeFalse();
        vm.IsInUseRemotely.Should().BeFalse();
        vm.CanConnect.Should().BeTrue();
        vm.CanDisconnect.Should().BeFalse();
        vm.CanRunPrimaryAction.Should().BeTrue();
        vm.PrimaryActionText.Should().Be("Подключить к этому ПК");
        vm.StatusText.Should().Be("Свободно");
        vm.StatusColor.Should().Be("#3FD69A");
    }

    [Fact]
    public void InUseLocallyDevice_ShouldHaveDisconnectActionTextAndState()
    {
        var device = new GeksaVirtualUsbDevice(
            Address: "12",
            DisplayName: "JaCarta Token",
            Vendor: "JaCarta",
            Product: "Token",
            ServerName: "Office USB",
            ServerAddress: "192.168.1.10:7575",
            SerialNumber: "789012",
            State: GeksaVirtualUsbState.InUseLocally,
            InUseBy: "local-user",
            AutoUseMode: "device",
            IsStorageDevice: false);

        var vm = new GeksaEcpDeviceViewModel(device, (_, _) => Task.CompletedTask);

        vm.IsAvailable.Should().BeFalse();
        vm.IsInUseLocally.Should().BeTrue();
        vm.IsInUseRemotely.Should().BeFalse();
        vm.CanConnect.Should().BeFalse();
        vm.CanDisconnect.Should().BeTrue();
        vm.CanRunPrimaryAction.Should().BeTrue();
        vm.PrimaryActionText.Should().Be("Отключить от этого ПК");
        vm.StatusText.Should().Be("Подключено к этому ПК");
        vm.StatusColor.Should().Be("#5B8DFF");
    }

    [Fact]
    public void InUseRemotelyDevice_ShouldHaveBusyActionTextAndBeDisabled()
    {
        var device = new GeksaVirtualUsbDevice(
            Address: "13",
            DisplayName: "Aladdin Token",
            Vendor: "Aladdin",
            Product: "Token",
            ServerName: "Office USB",
            ServerAddress: "192.168.1.10:7575",
            SerialNumber: "345678",
            State: GeksaVirtualUsbState.InUseRemotely,
            InUseBy: "employee",
            AutoUseMode: "port",
            IsStorageDevice: true);

        var vm = new GeksaEcpDeviceViewModel(device, (_, _) => Task.CompletedTask);

        vm.IsAvailable.Should().BeFalse();
        vm.IsInUseLocally.Should().BeFalse();
        vm.IsInUseRemotely.Should().BeTrue();
        vm.CanConnect.Should().BeFalse();
        vm.CanDisconnect.Should().BeFalse();
        vm.CanRunPrimaryAction.Should().BeFalse();
        vm.PrimaryActionText.Should().Be("Занято (employee)");
        vm.StatusText.Should().Be("Использует: employee");
        vm.StatusColor.Should().Be("#F3B35A");
        vm.IsStorageDevice.Should().BeTrue();
    }

    [Fact]
    public void UnavailableDevice_ShouldHaveUnavailableActionTextAndBeDisabled()
    {
        var device = new GeksaVirtualUsbDevice(
            Address: "14",
            DisplayName: "Unknown Token",
            Vendor: "Unknown",
            Product: "Token",
            ServerName: "Office USB",
            ServerAddress: "192.168.1.10:7575",
            SerialNumber: "",
            State: GeksaVirtualUsbState.Unavailable,
            InUseBy: string.Empty,
            AutoUseMode: "not-set",
            IsStorageDevice: false);

        var vm = new GeksaEcpDeviceViewModel(device, (_, _) => Task.CompletedTask);

        vm.CanRunPrimaryAction.Should().BeFalse();
        vm.PrimaryActionText.Should().Be("Недоступно");
        vm.StatusText.Should().Be("Недоступно");
        vm.StatusColor.Should().Be("#75849A");
    }
}
