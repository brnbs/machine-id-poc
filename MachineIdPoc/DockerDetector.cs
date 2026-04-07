using System.Text.RegularExpressions;

namespace MachineIdPoc;

/// <summary>
/// Detects whether the current process is running inside a Linux-based Docker container
/// using filesystem signals only — no environment variables, no runtime assumptions.
///
/// Signals checked (in order — first positive match wins):
///
///   1. /.dockerenv          — Docker has created this file in every container since v1.
///                             Not present on bare metal or in other runtimes.
///
///   2. /proc/2/status       — On any real Linux kernel, PID 2 is always kthreadd (the
///                             kernel thread daemon). Inside any container (Docker, LXC,
///                             Podman, …) PID 2 either does not exist or is a user-space
///                             process. This signal is cgroup-version-agnostic and
///                             works across all storage drivers and runtimes.
///
///   3. /proc/self/cgroup    — On cgroup v1 hosts, Docker sets cgroup paths that contain
///                             ":/docker/" or ":/kubepods/". Checked with a strict regex
///                             to avoid false positives.
///                             NOTE: On pure cgroup v2 hosts (Ubuntu 22.04+, Fedora 31+,
///                             Debian 11+) this file contains only "0::/" both on the
///                             host AND inside a container, so this signal is absent on
///                             those systems — covered by signals 1 and 2.
///
///   4. /proc/self/mountinfo — Fallback: overlay2 storage driver mounts show the host
///                             filesystem path (/var/lib/docker/overlay2/…) which
///                             contains "docker". Works only with overlay2; absent with
///                             devicemapper, btrfs, zfs, or rootless Docker variants.
/// </summary>
public static class DockerDetector
{
    public record DetectionResult(bool IsDocker, string Signal);

    // Matches cgroup v1 Docker/k8s paths like ":/docker/<id>" or ":/kubepods/…"
    private static readonly Regex CgroupDockerPattern =
        new(@":\/(docker|kubepods)(\/|$)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Checks four independent filesystem signals. Returns on the first positive match.
    /// </summary>
    public static DetectionResult Detect()
    {
        // ── Signal 1: /.dockerenv ─────────────────────────────────────────────
        if (File.Exists("/.dockerenv"))
            return new DetectionResult(true, "/.dockerenv exists");

        // ── Signal 2: /proc/2/status (kthreadd absence) ───────────────────────
        // On bare metal Linux, PID 2 is always "kthreadd".
        // Inside any container, PID 2 is absent or a user-space process.
        try
        {
            string proc2 = File.ReadAllText("/proc/2/status");
            if (!proc2.Contains("kthreadd", StringComparison.OrdinalIgnoreCase))
                return new DetectionResult(true, "/proc/2/status: PID 2 is not kthreadd");
        }
        catch (FileNotFoundException)
        {
            // PID 2 does not exist at all — definitive container signal.
            return new DetectionResult(true, "/proc/2/status: PID 2 absent");
        }
        catch { /* /proc not available — skip */ }

        // ── Signal 3: /proc/self/cgroup (cgroup v1 only) ──────────────────────
        // On cgroup v2 systems this file only contains "0::/" for both host and
        // container, so the regex will not match — that is expected and correct.
        try
        {
            string cgroup = File.ReadAllText("/proc/self/cgroup");
            if (CgroupDockerPattern.IsMatch(cgroup))
                return new DetectionResult(true, "/proc/self/cgroup matches docker/kubepods path");
        }
        catch { /* file absent or unreadable */ }

        // ── Signal 4: /proc/self/mountinfo (overlay2 storage driver only) ─────
        try
        {
            foreach (string line in File.ReadLines("/proc/self/mountinfo"))
            {
                if (line.Contains("overlay", StringComparison.OrdinalIgnoreCase) &&
                    line.Contains("/docker/", StringComparison.OrdinalIgnoreCase))
                    return new DetectionResult(true, "/proc/self/mountinfo has docker overlay mount");
            }
        }
        catch { /* file absent or unreadable */ }

        return new DetectionResult(false, "no container signals found");
    }
}
