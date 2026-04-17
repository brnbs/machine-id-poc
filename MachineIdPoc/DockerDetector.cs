using System.Text.RegularExpressions;

namespace MachineIdPoc;

/// <summary>
/// Detects whether the current process is running inside a Linux-based container
/// (Docker, containerd, ECS/Fargate) using filesystem signals only — no environment
/// variables, no runtime assumptions.
///
/// Signals checked (in order — first positive match wins):
///
///   1. /.dockerenv          — Docker daemon creates this file in every container since
///                             v1. Not present with containerd-only runtimes (e.g. AWS
///                             Fargate platform 1.4+) or on bare metal.
///
///   2. /proc/2/status       — On any real Linux kernel, PID 2 is always kthreadd (the
///                             kernel thread daemon). Inside a container where the
///                             runtime enforces PID namespace isolation (Docker, Podman,
///                             LXC, …) PID 2 is absent or is a user-space process.
///                             NOTE: On Fargate/Firecracker the container runs inside a
///                             microVM — the VM kernel's kthreadd IS at PID 2 from
///                             inside the container. This signal gives no match on those
///                             platforms; they are covered by signals 3 and 4 instead.
///                             Skipped on WSL2: the Hyper-V VM kernel does not expose
///                             kthreadd at PID 2, which would be a false positive on a
///                             bare-metal Windows developer machine running WSL2.
///
///   3. /proc/self/cgroup    — On cgroup v1 hosts, Docker sets cgroup paths that contain
///                             ":/docker/" or ":/kubepods/". ECS/Fargate sets paths
///                             under ":/ecs/". Checked with a strict regex.
///                             NOTE: On pure cgroup v2 hosts (Ubuntu 22.04+, Fedora 31+,
///                             Debian 11+) this file contains only "0::/" both on the
///                             host AND inside a container, so the regex will not match —
///                             covered by signals 1 and 2.
///
///   4. /proc/self/mountinfo — Fallback: overlay storage driver mounts show the host
///                             filesystem path, which contains "/docker/" (Docker daemon)
///                             or "/containerd/" (containerd/Fargate). Works only with
///                             overlay2; absent with devicemapper, btrfs, zfs, or rootless
///                             variants.
/// </summary>
public static class DockerDetector
{
    public record DetectionResult(bool IsDocker, string Signal);

    // Matches cgroup v1 Docker/k8s/ECS paths like ":/docker/<id>", ":/kubepods/…", ":/ecs/<id>"
    private static readonly Regex CgroupDockerPattern =
        new(@":\/(docker|kubepods|ecs)(\/|$)", RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Checks four independent filesystem signals. Returns on the first positive match.
    /// </summary>
    public static DetectionResult Detect()
    {
        // Signal 1: /.dockerenv
        if (File.Exists("/.dockerenv"))
            return new DetectionResult(true, "/.dockerenv exists");

        // Signal 2: /proc/2/status — on bare metal PID 2 is always kthreadd.
        // Inside a PID-namespace-isolated container it is absent or a user-space process.
        // Inconclusive on Fargate/Firecracker where the microVM kernel's kthreadd is visible.
        // Skipped on WSL2 to avoid false positives (WSL2's Hyper-V VM kernel does not expose
        // kthreadd at PID 2 either, but the process is not running in a container).
        bool skipKthreadd = false;
        try
        {
            string procVersion = File.ReadAllText("/proc/version");
            skipKthreadd = procVersion.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                           procVersion.Contains("WSL", StringComparison.OrdinalIgnoreCase);
        }
        catch { /* /proc not available */ }

        if (!skipKthreadd)
        {
            try
            {
                string proc2 = File.ReadAllText("/proc/2/status");
                if (!proc2.Contains("kthreadd", StringComparison.OrdinalIgnoreCase))
                    return new DetectionResult(true, "/proc/2/status: PID 2 is not kthreadd");
            }
            catch (FileNotFoundException)
            {
                // PID 2 absent — definitive container signal.
                return new DetectionResult(true, "/proc/2/status: PID 2 absent");
            }
            catch { /* /proc not available */ }
        }

        // Signal 3: /proc/self/cgroup — cgroup v1 only.
        // On cgroup v2 the file contains only "0::/" for both host and container, so no match.
        try
        {
            string cgroup = File.ReadAllText("/proc/self/cgroup");
            if (CgroupDockerPattern.IsMatch(cgroup))
                return new DetectionResult(true, "/proc/self/cgroup matches docker/kubepods/ecs path");
        }
        catch { /* file absent or unreadable */ }

        // Signal 4: /proc/self/mountinfo — overlay storage driver only.
        // Docker stores layers under /var/lib/docker/, containerd under /var/lib/containerd/.
        // We skip lines whose mountpoint (field 4) begins with those paths: on a host machine
        // running containers, those host-side layer mounts are visible in mountinfo too and
        // would otherwise cause a false positive for any developer with Docker running.
        try
        {
            foreach (string line in File.ReadLines("/proc/self/mountinfo"))
            {
                if (!line.Contains("overlay", StringComparison.OrdinalIgnoreCase))
                    continue;

                // mountinfo fields are space-separated; index 4 is the mountpoint.
                string[] fields = line.Split(' ');
                if (fields.Length < 5) continue;
                string mountPoint = fields[4];

                // Skip host-side container layer mounts to avoid false positives.
                if (mountPoint.StartsWith("/var/lib/docker/", StringComparison.OrdinalIgnoreCase) ||
                    mountPoint.StartsWith("/var/lib/containerd/", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (line.Contains("/docker/", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("/containerd/", StringComparison.OrdinalIgnoreCase))
                    return new DetectionResult(true, "/proc/self/mountinfo has overlay mount (docker/containerd)");
            }
        }
        catch { /* file absent or unreadable */ }

        return new DetectionResult(false, "no container signals found");
    }
}
