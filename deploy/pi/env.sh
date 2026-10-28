# Raspberry Pi test ortamının ortak ayarları (deploy/pi/*.sh bunu okur). Ortamdan ezilebilir.
PI_HOST="${PI_HOST:-mennano@192.168.1.60}"
PI_IP="${PI_IP:-192.168.1.60}"
# Pi'de her şey bu dizinin altında; sunucu konteyneri ve Caddy aynı yolu görür.
PI_BASE="${PI_BASE:-/home/mennano/abdera-pi}"
PI_ROOT="$PI_BASE/srv"
# Okullar <kısaltma>.<SCHOOLS_DOMAIN> adresinde, Cloudflare Tunnel üzerinden açılır.
SCHOOLS_DOMAIN="${SCHOOLS_DOMAIN:-miredo.app}"
TUNNEL_NAME="${TUNNEL_NAME:-abdera-pi}"
# Mac'te çalışan platform API'si; Pi'deki okullar alarm webhook'unu buraya atar.
MAC_IP="${MAC_IP:-$(ipconfig getifaddr en0 2>/dev/null || echo 127.0.0.1)}"
PLATFORM_DB_PORT="${PLATFORM_DB_PORT:-5440}"
