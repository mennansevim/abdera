#!/usr/bin/env bash
# Ajan testlerini Linux konteynerinde çalıştırır (flock, GNU stat). CI ve yerel geliştirme için.
set -euo pipefail
DEPLOY="$(cd "$(dirname "$0")/../.." && pwd)"
exec docker run --rm -v "$DEPLOY:/deploy:ro" alpine:3.20 sh -c \
  'apk add --no-cache -q bash util-linux coreutils openssl gzip diffutils >/dev/null && bash /deploy/host/test/abdera-host.test.sh'
