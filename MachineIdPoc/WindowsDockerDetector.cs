using System.Runtime.Versioning;
using Microsoft.Win32;

namespace MachineIdPoc;

/// <summary>
/// Detects whether the current process is running inside a Windows-based Docker container
/// (process-isolated) using OS-level signals only — no environment variables required.
///
/// Signals checked (in order — first positive match wins):
///
///   1. cexecsvc registry key      — cexecsvc (Container Execution Agent Service) is the
///                                   Windows container runtime management service that Docker
///                                   HCS places inside every Windows container's isolated
///                                   registry namespace. The key exists at:
///                                     HKLM\SYSTEM\CurrentControlSet\Services\cexecsvc
///                                   Inside a container this key is present in the container's
///                                   isolated registry view. On a bare-metal host (even with
///                                   Docker installed), cexecsvc is launched programmatically
///                                   by HCS — it is NOT registered as a persistent SCM service
///                                   in the host registry.
///                                   NOTE: Only fires for process-isolated containers (HCS
///                                   Silo). Hyper-V isolated containers (e.g. AWS Fargate)
///                                   run inside a Utility VM — no Silo is created, so this
///                                   key is absent.
///
///   2. Control\Containers registry key — HKLM\SYSTEM\CurrentControlSet\Control\Containers
///                                   is baked into every Windows container base image
///                                   (Server Core, Nano Server) by Microsoft. It is absent
///                                   on bare-metal Windows Server hosts regardless of
///                                   Docker being installed. Unlike cexecsvc it is present
///                                   in BOTH process-isolated and Hyper-V isolated containers
///                                   because it lives in the container image's own registry
///                                   hive, not in an HCS-managed Silo.
///
/// NOTE: C:\.dockerenv is NOT created by Docker for Windows containers — that file only
/// exists in Linux containers. The equivalent Windows signal is the cexecsvc registry key.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsDockerDetector
{
    public record DetectionResult(bool IsDocker, string Signal);

    /// <summary>
    /// Checks two independent signals. Returns on the first positive match.
    /// </summary>
    public static DetectionResult Detect()
    {
        // Signal 1: cexecsvc registry key — present in the isolated registry of every
        // process-isolated Windows container, absent on bare-metal hosts.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\cexecsvc",
                writable: false);
            if (key != null)
                return new DetectionResult(true, "cexecsvc service registry key present");
        }
        catch { /* registry access failure */ }

        // Signal 4: Control\Containers registry key — baked into every Windows container
        // base image by Microsoft. Absent on bare-metal hosts. Covers both process-isolated
        // and Hyper-V isolated (Fargate) containers.
        try
        {
            using var containersKey = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Control\Containers",
                writable: false);
            if (containersKey != null)
                return new DetectionResult(true, @"HKLM\…\Control\Containers registry key present");
        }
        catch { /* registry access failure */ }

        return new DetectionResult(false, "no Windows container signals found");
    }
}

