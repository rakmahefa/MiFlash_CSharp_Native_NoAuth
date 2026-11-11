using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using MiFlash.Cli.i18n;
using MiFlash.Transport.Usb;
using Spectre.Console;
using System.CommandLine;

namespace MiFlash.Cli.Commands;

/// <summary>
/// <c>miflash devices [--json] [--watch]</c> (M4.2, RF-1/RF-2) — liste des
/// appareils EDL 9008 et fastboot. <c>--json</c> : sortie machine ; <c>--watch</c> :
/// surveillance insert/remove (polling 1 s, Ctrl+C pour arrêter).
/// </summary>
internal static class DevicesCommand
{
    public static Command Build()
    {
        var json = new Option<bool>("--json", new[] { "-j" }) { Description = "machine-readable JSON output" };
        var watch = new Option<bool>("--watch", new[] { "-w" }) { Description = "watch for device changes (Ctrl+C to stop)" };

        var command = new Command("devices", "list connected EDL/fastboot devices");
        command.Add(json);
        command.Add(watch);
        command.SetAction(parseResult => Run(
            parseResult.GetValue(json),
            parseResult.GetValue(watch)));
        return command;
    }

    private static int Run(bool json, bool watch)
    {
        if (watch)
        {
            return Watch(json);
        }
        using var enumerator = new UsbDeviceEnumerator();
        List<UsbDeviceIdentity> devices = enumerator.ListDevices();
        if (json)
        {
            Console.WriteLine(JsonSerializer.Serialize(devices.Select(ToJson).ToList(), JsonOpts));
            return 0;
        }

        AnsiConsole.MarkupLine("[bold]" + EdlTargets.Escape(L._("Cli.DevicesHeader")) + "[/]");
        if (devices.Count == 0)
        {
            AnsiConsole.MarkupLine("[grey]  " + EdlTargets.Escape(L._("Cli.NoDevice")) + "[/]");
            return 0;
        }
        var table = new Table().Border(TableBorder.Rounded);
        table.AddColumn("kind");
        table.AddColumn("usb");
        table.AddColumn("location");
        table.AddColumn("serial");
        table.AddColumn("product");
        foreach (UsbDeviceIdentity d in devices)
        {
            table.AddRow(
                d.Kind == UsbDeviceKind.Edl9008 ? "[yellow]EDL 9008[/]" : "[green]fastboot[/]",
                $"{d.VendorId:x4}:{d.ProductId:x4}",
                d.Location,
                d.SerialNumber,
                d.Product);
        }
        AnsiConsole.Write(table);
        return 0;
    }

    private static int Watch(bool json)
    {
        AnsiConsole.MarkupLine("[grey]" + EdlTargets.Escape(L._("Cli.WatchHint")) + "[/]");
        using var watcher = new UsbDeviceWatcher { Interval = TimeSpan.FromSeconds(1) };
        watcher.DeviceArrived += d => WriteLine(json, "arrived", d);
        watcher.DeviceLeft += d => WriteLine(json, "left", d);
        watcher.Start();
        var done = new ManualResetEventSlim(false);
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; done.Set(); };
        done.Wait();
        watcher.Stop();
        return 0;
    }

    private static void WriteLine(bool json, string what, UsbDeviceIdentity d)
    {
        if (json)
        {
            var payload = new Dictionary<string, object> { ["event"] = what, ["device"] = ToJson(d) };
            Console.WriteLine(JsonSerializer.Serialize(payload, JsonOpts));
        }
        else
        {
            string key = what == "arrived" ? "Cli.DeviceArrived" : "Cli.DeviceLeft";
            string color = what == "arrived" ? "green" : "red";
            AnsiConsole.MarkupLine($"[{color}]{EdlTargets.Escape(L._(key))}{EdlTargets.Escape(d.ToString())}[/]");
        }
    }

    private static object ToJson(UsbDeviceIdentity d)
    {
        return new
        {
            kind = d.Kind == UsbDeviceKind.Edl9008 ? "edl-9008" : "fastboot",
            vendorId = d.VendorId.ToString("x4"),
            productId = d.ProductId.ToString("x4"),
            bus = d.BusNumber,
            port = d.PortNumber,
            location = d.Location,
            serial = d.SerialNumber,
            product = d.Product,
            manufacturer = d.Manufacturer,
        };
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
    };
}
