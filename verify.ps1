$ErrorActionPreference = "Stop"

$IMAGE = "machine-id-poc"

Write-Host "================================================================"
Write-Host " Building Docker image: $IMAGE"
Write-Host "================================================================"
docker build -t $IMAGE .
if ($LASTEXITCODE -ne 0) { exit 1 }

Write-Host ""
Write-Host "================================================================"
Write-Host " Full diagnostic output -- Container 1"
Write-Host "================================================================"
$OUTPUT1 = docker run --rm $IMAGE
if ($LASTEXITCODE -ne 0) { Write-Error "Container 1 failed"; exit 1 }
Write-Host $OUTPUT1
$ID1 = ($OUTPUT1 -split "`n" | Where-Object { $_ -match "^DEVICE_ID=" }) -replace "^DEVICE_ID=", ""

Write-Host ""
Write-Host "================================================================"
Write-Host " Container 2 (Device ID only)"
Write-Host "================================================================"
$OUTPUT2 = docker run --rm $IMAGE
if ($LASTEXITCODE -ne 0) { Write-Error "Container 2 failed"; exit 1 }
$ID2 = ($OUTPUT2 -split "`n" | Where-Object { $_ -match "^DEVICE_ID=" }) -replace "^DEVICE_ID=", ""
Write-Host "DEVICE_ID=$ID2"

Write-Host ""
Write-Host "================================================================"
Write-Host " Container 3 (Device ID only)"
Write-Host "================================================================"
$OUTPUT3 = docker run --rm $IMAGE
if ($LASTEXITCODE -ne 0) { Write-Error "Container 3 failed"; exit 1 }
$ID3 = ($OUTPUT3 -split "`n" | Where-Object { $_ -match "^DEVICE_ID=" }) -replace "^DEVICE_ID=", ""
Write-Host "DEVICE_ID=$ID3"

Write-Host ""
Write-Host "================================================================"
Write-Host " Verification"
Write-Host "================================================================"
Write-Host "  Container 1: $ID1"
Write-Host "  Container 2: $ID2"
Write-Host "  Container 3: $ID3"
Write-Host ""

$ID1 = $ID1.Trim()
$ID2 = $ID2.Trim()
$ID3 = $ID3.Trim()

if ($ID1 -eq $ID2 -and $ID2 -eq $ID3 -and $ID1 -ne "") {
    Write-Host "  PASS: All 3 containers produced the same non-empty Device ID."
    exit 0
} else {
    Write-Error "  FAIL: Device IDs differ or are empty!"
    exit 1
}
