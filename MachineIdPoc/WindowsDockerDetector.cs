using System.Runtime.InteropServices;
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
///
///   2. Job Object KILL_ON_JOB_CLOSE — Windows containers run all processes inside a Job
///                                   Object with JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE set.
///                                   This P/Invoke signal queries the current process's
///                                   Job Object limits and checks for this flag combination.
///                                   While other software also uses Job Objects, the
///                                   container-specific combination of flags is distinctive.
///
/// NOTE: C:\.dockerenv is NOT created by Docker for Windows containers — that file only
/// exists in Linux containers. The equivalent Windows signal is the cexecsvc registry key.
/// </summary>
[SupportedOSPlatform("windows")]
public static class WindowsDockerDetector
{
    public record DetectionResult(bool IsDocker, string Signal);

    // Job Object constants
    private const int JobObjectBasicLimitInformation = 2;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public nuint MinimumWorkingSetSize;
        public nuint MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public nuint Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool IsProcessInJob(
        IntPtr hProcess,
        IntPtr hJob,
        out bool result);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool QueryInformationJobObject(
        IntPtr hJob,
        int jobObjectInfoClass,
        out JOBOBJECT_BASIC_LIMIT_INFORMATION lpJobObjectInfo,
        uint cbJobObjectInfoLength,
        out uint lpReturnLength);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    /// <summary>
    /// Checks two independent signals. Returns on the first positive match.
    /// </summary>
    public static DetectionResult Detect()
    {
        // ── Signal 1: cexecsvc registry key ───────────────────────────────────
        // The Container Execution Agent Service key is present in the isolated
        // registry overlay of every Windows container, but NOT on the bare-metal
        // host registry (even with Docker installed).
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SYSTEM\CurrentControlSet\Services\cexecsvc",
                writable: false);
            if (key != null)
                return new DetectionResult(true, "cexecsvc service registry key present");
        }
        catch { /* registry access failure — skip */ }

        // ── Signal 2: Job Object KILL_ON_JOB_CLOSE flag ───────────────────────
        // Windows containers assign processes to a Job Object with the
        // JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE flag set, ensuring all container
        // processes are terminated when Docker removes the container.
        try
        {
            if (IsProcessInJob(GetCurrentProcess(), IntPtr.Zero, out bool inJob) && inJob)
            {
                if (QueryInformationJobObject(
                    IntPtr.Zero,
                    JobObjectBasicLimitInformation,
                    out JOBOBJECT_BASIC_LIMIT_INFORMATION info,
                    (uint)Marshal.SizeOf<JOBOBJECT_BASIC_LIMIT_INFORMATION>(),
                    out _))
                {
                    if ((info.LimitFlags & JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE) != 0)
                        return new DetectionResult(true, "Job Object has KILL_ON_JOB_CLOSE flag (container Job Object)");
                }
            }
        }
        catch { /* P/Invoke failure — skip */ }

        return new DetectionResult(false, "no Windows container signals found");
    }
}

