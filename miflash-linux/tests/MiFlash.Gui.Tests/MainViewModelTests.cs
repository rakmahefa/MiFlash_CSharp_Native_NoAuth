using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using MiFlash.Edl;
using MiFlash.Gui.Services;
using MiFlash.Gui.ViewModels;
using MiFlash.Transport.Usb;

namespace MiFlash.Gui.Tests;

/// <summary>
/// Tests headless du GUI (M6.3) : ViewModels sans Avalonia — fournisseurs
/// USB/EDL remplacés par des fakes, marshaling immédiat.
/// </summary>
public class MainViewModelTests
{
    private static UsbDeviceIdentity EdlDevice(string location, string serial = "")
    {
        return new UsbDeviceIdentity
        {
            VendorId = 0x05c6,
            ProductId = 0x9008,
            BusNumber = byte.Parse(location.Split('.')[0]),
            PortNumber = byte.Parse(location.Split('.')[1]),
            SerialNumber = serial,
            Kind = UsbDeviceKind.Edl9008,
        };
    }

    private static UsbDeviceIdentity FastbootDevice(string serial)
    {
        return new UsbDeviceIdentity
        {
            VendorId = 0x18d1,
            ProductId = 0x4ee0,
            BusNumber = 2,
            PortNumber = 4,
            SerialNumber = serial,
            Kind = UsbDeviceKind.Fastboot,
        };
    }

    [Fact]
    public void Refresh_lists_edl_and_fastboot_devices()
    {
        var vm = new MainViewModel(deviceProvider: () => new List<UsbDeviceIdentity>
        {
            EdlDevice("1.3", "SN123"),
            FastbootDevice("FASTSN"),
        });

        vm.Refresh();

        Assert.Equal(2, vm.Devices.Count);
        Assert.Equal("9008@1.3#SN123", vm.Devices[0].DisplayName);
        Assert.True(vm.Devices[0].IsEdl);
        Assert.Equal("fastboot@2.4#FASTSN", vm.Devices[1].DisplayName);
        Assert.False(vm.Devices[1].IsEdl);
        Assert.Same(vm.Devices[0], vm.SelectedDevice);
        Assert.Contains(vm.LogLines, l => l.Contains("1 appareil(s) EDL 9008"));
    }

    [Fact]
    public void Refresh_handles_enumeration_failure()
    {
        var vm = new MainViewModel(deviceProvider: () => throw new InvalidOperationException("no libusb"));
        vm.Refresh();
        Assert.Empty(vm.Devices);
        Assert.Contains(vm.LogLines, l => l.Contains("énumération USB impossible"));
    }

    [Fact]
    public void RunEdl_rejects_invalid_directory()
    {
        var runnerCalled = false;
        var vm = new MainViewModel(
            deviceProvider: () => new List<UsbDeviceIdentity> { EdlDevice("1.3") },
            edlRunner: _ => { runnerCalled = true; return Array.Empty<EdlJobResult>(); });
        vm.RomDirectory = "/répertoire/inexistant";
        vm.Confirmed = true;

        vm.RunEdl(EdlOperation.Flash, destructive: true);

        Assert.False(runnerCalled);
        Assert.Contains(vm.LogLines, l => l.Contains("répertoire ROM invalide"));
    }

    [Fact]
    public void RunEdl_requires_confirmation_for_destructive_operations()
    {
        string dir = Directory.CreateTempSubdirectory("miflash-gui-").FullName;
        try
        {
            var runnerCalled = false;
            var vm = new MainViewModel(
                deviceProvider: () => new List<UsbDeviceIdentity> { EdlDevice("1.3") },
                edlRunner: _ => { runnerCalled = true; return Array.Empty<EdlJobResult>(); });
            vm.RomDirectory = dir;
            vm.Confirmed = false;

            vm.RunEdl(EdlOperation.Flash, destructive: true);

            Assert.False(runnerCalled);
            Assert.Contains(vm.LogLines, l => l.Contains("confirmation"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RunEdl_builds_job_for_selected_device()
    {
        string dir = Directory.CreateTempSubdirectory("miflash-gui-").FullName;
        try
        {
            var done = new ManualResetEventSlim(false);
            EdlJob capturedJob = null;
            var vm = new MainViewModel(
                deviceProvider: () => new List<UsbDeviceIdentity> { EdlDevice("1.3", "SN1"), EdlDevice("2.1", "SN2") },
                edlRunner: jobs =>
                {
                    capturedJob = jobs.Single();
                    done.Set();
                    return new[] { new EdlJobResult { DeviceName = capturedJob.DeviceName, Success = true, Message = "ok" } };
                });
            vm.RomDirectory = dir;
            vm.Confirmed = true;
            vm.Refresh();
            vm.SelectedDevice = vm.Devices.First(d => d.DisplayName.StartsWith("9008@2.1"));
            vm.ProgrammerIndex = 3; // ddr5

            vm.RunEdl(EdlOperation.Flash, destructive: true);

            Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "runner non appelé");
            Assert.NotNull(capturedJob);
            Assert.Equal("9008@2.1#SN2", capturedJob.DeviceName);
            Assert.Equal(dir, capturedJob.SwPath);
            Assert.Equal(EdlOperation.Flash, capturedJob.Operation);
            Assert.NotNull(capturedJob.TransportFactory);
            Assert.NotNull(capturedJob.Configure);
            // IsBusy retombe après le rapport (marshaling immédiat).
            Assert.True(SpinWait.SpinUntil(() => !vm.IsBusy, TimeSpan.FromSeconds(10)));
            Assert.Contains(vm.LogLines, l => l.Contains("9008@2.1#SN2: succès"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RunEdl_without_selection_targets_all_edl_devices()
    {
        string dir = Directory.CreateTempSubdirectory("miflash-gui-").FullName;
        try
        {
            var done = new ManualResetEventSlim(false);
            int jobCount = -1;
            var vm = new MainViewModel(
                deviceProvider: () => new List<UsbDeviceIdentity> { EdlDevice("1.3", "SN1"), FastbootDevice("FB1") },
                edlRunner: jobs =>
                {
                    jobCount = jobs.Count();
                    done.Set();
                    return Array.Empty<EdlJobResult>();
                });
            vm.RomDirectory = dir;
            vm.Confirmed = true;
            vm.Refresh();
            vm.SelectedDevice = null; // aucune sélection → tous les 9008

            vm.RunEdl(EdlOperation.Backup, destructive: false);

            Assert.True(done.Wait(TimeSpan.FromSeconds(10)), "runner non appelé");
            Assert.Equal(1, jobCount); // le fastboot n'est pas une cible EDL
            Assert.True(SpinWait.SpinUntil(() => !vm.IsBusy, TimeSpan.FromSeconds(10)));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RunEdl_reports_when_no_edl_device()
    {
        string dir = Directory.CreateTempSubdirectory("miflash-gui-").FullName;
        try
        {
            var runnerCalled = false;
            var vm = new MainViewModel(
                deviceProvider: () => new List<UsbDeviceIdentity> { FastbootDevice("FB1") },
                edlRunner: _ => { runnerCalled = true; return Array.Empty<EdlJobResult>(); });
            vm.RomDirectory = dir;
            vm.Confirmed = true;

            vm.RunEdl(EdlOperation.Flash, destructive: true);

            Assert.False(runnerCalled);
            Assert.Contains(vm.LogLines, l => l.Contains("aucun appareil EDL 9008"));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void RunFastboot_requires_fastboot_binary()
    {
        string dir = Directory.CreateTempSubdirectory("miflash-gui-").FullName;
        try
        {
            var vm = new MainViewModel(fastbootLocator: () => null);
            vm.RomDirectory = dir;
            vm.Confirmed = true;

            vm.RunFastboot(MiFlash.Fastboot.FlashScript.Variant.CleanAll);

            Assert.Contains(vm.LogLines, l => l.Contains("fastboot introuvable"));
            Assert.False(vm.IsBusy);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void AddLog_is_threadsafe_and_capped()
    {
        var vm = new MainViewModel();
        for (int i = 0; i < 2100; i++)
        {
            vm.AddLog("ligne " + i);
        }
        Assert.Equal(2000, vm.LogLines.Count);
        Assert.StartsWith("[", vm.LogLines[0]);
        Assert.Contains("ligne 2099", vm.LogLines[^1]);
    }
}

/// <summary>Rapprochement reporter ↔ appareil (tolérant aux clés du moteur).</summary>
public class DeviceViewModelMatchingTests
{
    [Theory]
    [InlineData("9008@1.3#SN1", true)]      // DeviceName exact
    [InlineData("SN1", true)]                // serial exact
    [InlineData("05c6:9008 bus 1 port 3", true)] // PortName du transport USB
    [InlineData("9008@9.9", false)]
    [InlineData("autre", false)]
    public void Matches_resolves_engine_keys(string key, bool expected)
    {
        var device = new DeviceViewModel("9008@1.3#SN1", isEdl: true, location: "1.3", serialNumber: "SN1");
        Assert.Equal(expected, device.Matches(key));
    }

    [Fact]
    public void Progress_is_clamped()
    {
        var device = new DeviceViewModel("x", true, "1.3", "");
        device.Progress = 1.5;
        Assert.Equal(1, device.Progress);
        device.Progress = -0.5;
        Assert.Equal(0, device.Progress);
    }
}

/// <summary>GuiReporter : relais thread-safe des statuts du moteur.</summary>
public class GuiReporterTests
{
    [Fact]
    public void UpdateDeviceStatus_raises_event_with_values()
    {
        using var reporter = new GuiReporter();
        GuiReporter.Status received = null;
        reporter.Received += status => received = status;

        reporter.UpdateDeviceStatus("9008@1.3", 0.5f, "flashing system", "flashing", isDone: false);

        Assert.NotNull(received);
        Assert.Equal("9008@1.3", received.DeviceName);
        Assert.Equal(0.5f, received.Progress);
        Assert.Equal("flashing system", received.StatusText);
        Assert.False(received.IsDone);
    }
}
