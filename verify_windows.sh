#!/usr/bin/env bash
set -euo pipefail

IMAGE="machine-id-poc-win"

echo "================================================================"
echo " Full diagnostic output -- Container 1"
echo "================================================================"
OUTPUT1=$(docker run --rm "$IMAGE")
echo "$OUTPUT1"
ID1=$(echo "$OUTPUT1" | grep '^DEVICE_ID=' | cut -d= -f2 | tr -d '\r')

echo ""
echo "================================================================"
echo " Container 2 (Device ID only)"
echo "================================================================"
OUTPUT2=$(docker run --rm "$IMAGE")
ID2=$(echo "$OUTPUT2" | grep '^DEVICE_ID=' | cut -d= -f2 | tr -d '\r')
echo "DEVICE_ID=$ID2"

echo ""
echo "================================================================"
echo " Container 3 (Device ID only)"
echo "================================================================"
OUTPUT3=$(docker run --rm "$IMAGE")
ID3=$(echo "$OUTPUT3" | grep '^DEVICE_ID=' | cut -d= -f2 | tr -d '\r')
echo "DEVICE_ID=$ID3"

echo ""
echo "================================================================"
echo " Verification"
echo "================================================================"
echo "  Container 1: $ID1"
echo "  Container 2: $ID2"
echo "  Container 3: $ID3"
echo ""

if [[ "$ID1" == "$ID2" && "$ID2" == "$ID3" && -n "$ID1" ]]; then
    echo "  PASS: All 3 containers produced the same non-empty Device ID."
    exit 0
else
    echo "  FAIL: Device IDs differ or are empty!"
    exit 1
fi
