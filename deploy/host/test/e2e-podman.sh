#!/usr/bin/env bash
# e2e-real-docker.sh'i macOS + Podman'da çalıştırır: ajan, Podman soketine bağlı bir Linux konteynerinde
# koşar. Çalışma dizini VM'de; Caddy'nin bind mount'u aynı yolu görsün diye konteynere aynı yolla bağlanır.
set -euo pipefail
DEPLOY="$(cd "$(dirname "$0")/../.." && pwd)"
VM_UID="$(podman machine ssh 'id -u' | tr -d '\r')"
SOCK="/run/user/$VM_UID/podman/podman.sock"
WORK=/var/tmp/abdera-e2e
podman machine ssh "mkdir -p $WORK"
exec docker run --rm --security-opt label=disable \
  -v "$SOCK:/var/run/docker.sock" -v "$WORK:$WORK" -v "$DEPLOY:/deploy:ro" -e E2E_WORK="$WORK" -e E2E_DOCKER_RUN_OPTS="--security-opt label=disable" \
  docker.io/library/docker:27.3.1-cli sh -c \
  'apk add --no-cache -q bash util-linux coreutils openssl gzip >/dev/null && bash /deploy/host/test/e2e-real-docker.sh'
