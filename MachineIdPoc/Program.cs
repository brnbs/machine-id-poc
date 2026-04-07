using System.Runtime.InteropServices;
using DeviceId;
using DeviceId.Linux;
using MachineIdPoc;
using MachineIdPoc.Components;

Console.WriteLine("=== Machine ID PoC ===");
Console.WriteLine($"Container hostname : {Environment.MachineName}");
Console.WriteLine();

// ── Docker detection ─────────────────────────────────────────────────────────
bool isDocker;
string detectionSignal;

if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
{
    var wd = WindowsDockerDetector.Detect();
    isDocker = wd.IsDocker;
    detectionSignal = wd.Signal;
}
else
{
    var ld = DockerDetector.Detect();
    isDocker = ld.IsDocker;
    detectionSignal = ld.Signal;
}

if (isDocker)
    Console.WriteLine($"[OK  ] Running inside a Docker container ({detectionSignal})");
else
    Console.WriteLine($"[WARN] NOT running inside a Docker container ({detectionSignal})");
Console.WriteLine("      Device ID will only be host-unique when running in Docker.");
Console.WriteLine();

Console.WriteLine("--- Component diagnostics ---");

// ArpGatewayMacComponent caches its value so GetValue() can be called by
// the DeviceIdBuilder later without triggering a second resolution.
var arpComponent = new ArpGatewayMacComponent();

// Read raw signals for diagnostic visibility
Diag("ProductUuid   (DMI) ", ReadFile("/sys/class/dmi/id/product_uuid"));
Diag("BoardSerial   (DMI) ", ReadFile("/sys/class/dmi/id/board_serial"));
Diag("GatewayMAC    (ARP) ", arpComponent.GetValue());
Diag("CpuInfo    (sample) ", CpuSample());

// ── Final Device ID ──────────────────────────────────────────────────────────
// Components used:
//   AddProductUuid()          -> /sys/class/dmi/id/product_uuid  (host SMBIOS UUID)
//   AddMotherboardSerialNumber -> /sys/class/dmi/id/board_serial  (host board serial)
//   AddCpuInfo()              -> /proc/cpuinfo                    (host CPU; shared kernel)
//   ArpGatewayMacComponent    -> /proc/net/arp gateway entry      (docker0 bridge MAC)
//
// NOTE: AddMachineId() and AddDockerContainerId() are intentionally NOT used
// because they return per-container values (/etc/machine-id, /proc/1/cgroup).

Console.WriteLine();
Console.WriteLine("--- Final Device ID ---");

string deviceId = new DeviceIdBuilder()
    .OnLinux(linux => linux
        .AddProductUuid()
        .AddMotherboardSerialNumber()
        .AddCpuInfo())
    .AddComponent(arpComponent.Name, arpComponent)
    .ToString();

Console.WriteLine($"DEVICE_ID={deviceId}");

// ── Helpers ──────────────────────────────────────────────────────────────────

static void Diag(string label, string? value)
{
    bool ok = !string.IsNullOrWhiteSpace(value);
    Console.WriteLine($"  [{(ok ? "OK  " : "MISS")}] {label}: {(ok ? value!.Trim() : "(unavailable)")}");
}

static string? ReadFile(string path)
{
    try   { return File.ReadAllText(path).Trim(); }
    catch { return null; }
}

static string CpuSample()
{
    try
    {
        // Extract a couple of stable fields for the diagnostic line.
        // The full /proc/cpuinfo content is used by AddCpuInfo() in the final hash.
        // Field names differ between x86 ("vendor_id", "model name") and ARM
        // ("CPU implementer", "Hardware", "Model name").
        var lines = File.ReadLines("/proc/cpuinfo")
            .Where(l => l.StartsWith("vendor_id")     ||
                        l.StartsWith("model name")    ||
                        l.StartsWith("Model name")    ||
                        l.StartsWith("CPU implementer")||
                        l.StartsWith("Hardware"))
            .Take(2)
            .Select(l => l.Split(':').Last().Trim());
        var result = string.Join(", ", lines);
        return string.IsNullOrWhiteSpace(result) ? "(no matching fields)" : result;
    }
    catch { return "(unavailable)"; }
}
