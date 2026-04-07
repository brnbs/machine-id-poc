# ── Stage 1: build ───────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY MachineIdPoc/ .
RUN dotnet restore
RUN dotnet publish -c Release -o /app

# ── Stage 2: runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS final
WORKDIR /app
COPY --from=build /app .

# Default Docker containers run as root — this is intentional here because
# /sys/class/dmi/id/product_uuid has mode 0400 and requires root to read.
# No extra capabilities or bind mounts are needed.
ENTRYPOINT ["dotnet", "MachineIdPoc.dll"]
