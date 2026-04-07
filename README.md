# Machine ID PoC

A .NET 8 proof-of-concept that generates a **stable, host-unique Device ID from inside a standard Docker container** — no special capabilities, bind mounts, or environment variables required.

## How it works

The ID is a SHA-256 hash of whichever host-level signals are readable inside the container:

| Component | Source | Availability |
|---|---|---|
| **Gateway MAC** (primary) | `/proc/net/arp` — MAC of the docker0 bridge, which is host-specific | Always available on default Docker bridge networks |
| Product UUID | `/sys/class/dmi/id/product_uuid` | Bare-metal Linux hosts; not available in Docker Desktop or VMs |
| Motherboard serial | `/sys/class/dmi/id/board_serial` | Same as above |
| CPU info | `/proc/cpuinfo` | Always available; stable per host, low uniqueness alone |

The diagnostic output shows `[OK]` / `[MISS]` for each component so you can see exactly what contributed to the ID.

## Requirements

- Docker

## Run locally

**Linux / macOS:**
```bash
./verify.sh
```

**Windows 11** (PowerShell — Docker Desktop required):
```powershell
.\verify.ps1
```

> If script execution is blocked, run `Set-ExecutionPolicy -Scope CurrentUser RemoteSigned` once in an elevated PowerShell prompt.

Both scripts build the image and run 3 containers back-to-back, asserting they all produce the same Device ID.

## Testing on other devices

**Goal:** confirm the ID is the same across containers on the same host but different across different hosts.

### Step 1 — Record the ID on your first machine

```bash
# Linux/macOS
./verify.sh

# Windows
.\verify.ps1
```

Note the `DEVICE_ID=` line printed at the end.

### Step 2 — Run on a second machine

Copy the repo to a second machine (or SSH into one), then:

```bash
docker build -t machine-id-poc .
docker run --rm machine-id-poc
```

Compare the `DEVICE_ID=` line with the one from Step 1 — **it must differ**.

### Step 3 — Confirm stability across containers on the second machine

```bash
# Linux/macOS
./verify.sh

# Windows
.\verify.ps1
```

All 3 containers must print the same `DEVICE_ID`.

### Tips

- On **bare-metal Linux** the DMI components (`ProductUuid`, `BoardSerial`) will also show `[OK]`, giving additional host entropy on top of the gateway MAC.
- On **Docker Desktop** (Mac/Windows) the Linux VM abstracts the SMBIOS layer, so only the gateway MAC and CPU info contribute. The docker0 bridge MAC is still unique per Docker Desktop installation.
- If you restart Docker Desktop, the docker0 bridge MAC may change (it's regenerated on daemon start). This is expected — the ID ties to the running Docker daemon's bridge, not to hardware.

## Running outside Docker (diagnostic / development)

You can run the app directly with the .NET SDK — no Docker required. This is useful for verifying that the container detection warning fires correctly.

**Requires:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

```bash
cd MachineIdPoc
dotnet run
```

### Expected output on macOS

```
=== Machine ID PoC ===
Container hostname : your-hostname

[WARN] NOT running inside a Docker container (no container signals found)
      Device ID will only be host-unique when running in Docker.

--- Component diagnostics ---
  [MISS] ProductUuid   (DMI) : (unavailable)
  [MISS] BoardSerial   (DMI) : (unavailable)
  [MISS] GatewayMAC    (ARP) : (unavailable)
  [OK  ] CpuInfo    (sample) : (unavailable)

--- Final Device ID ---
DEVICE_ID=<hash of empty strings — not host-unique>
```

All components show `[MISS]` because `/proc`, `/sys/class/dmi/id`, and `/proc/net/arp` don't exist on macOS. The Device ID is still produced (it's a hash of empty component values) but is **not host-unique** — every macOS machine returns the same value in this case.

### Expected output on bare-metal Linux (outside Docker)

```
=== Machine ID PoC ===
Container hostname : your-hostname

[WARN] NOT running inside a Docker container (no container signals found)
      Device ID will only be host-unique when running in Docker.

--- Component diagnostics ---
  [OK  ] ProductUuid   (DMI) : <smbios-uuid>        # if run as root
  [OK  ] BoardSerial   (DMI) : <board-serial>        # if run as root
  [OK  ] GatewayMAC    (ARP) : <router-mac>          # your network router, not docker0
  [OK  ] CpuInfo    (sample) : <cpu-vendor>, <cpu-model>

--- Final Device ID ---
DEVICE_ID=<hash>
```

> Note: `GatewayMAC` resolves your **physical router's** MAC address when run outside Docker, not the docker0 bridge. Machines on the same LAN share the same router MAC — so the ID is **less unique** than the Docker version. The DMI signals (`ProductUuid`, `BoardSerial`) compensate when available, but require running as root.
