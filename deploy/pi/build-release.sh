#!/usr/bin/env bash
# Okul imajlarını Mac'te (arm64, Pi ile aynı mimari) derler, Pi'deki depoya iter ve sürüm kaydını üretir.
# Pi depoya kendi içinden localhost:5050 diye eriştiği için (Docker güvensiz depo ayarı gerekmez) sürüm
# kaydındaki imaj adresleri localhost:5050'ye çevrilir; digest aynıdır.
# Kullanım: deploy/pi/build-release.sh 2026.10.28.1
set -euo pipefail
VERSION="${1:?sürüm gerekli (YYYY.MM.DD.N)}"
HERE="$(cd "$(dirname "$0")" && pwd)"
# shellcheck source=env.sh
. "$HERE/env.sh"
LOCAL_REGISTRY="$PI_IP:5050" LOCAL_OUT="$HERE/.out" "$HERE/../local/build-release.sh" "$VERSION"
sed -i '' "s#\"$PI_IP:5050/#\"localhost:5050/#g" "$HERE/.out/release-$VERSION.json"
echo "Sürüm kaydı (Pi için): $HERE/.out/release-$VERSION.json"
