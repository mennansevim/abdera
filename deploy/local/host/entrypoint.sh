#!/bin/sh
# Ajan ve şablon her açılışta repodan kurulur (/deploy salt okunur bağlı); betik değişince konteyneri
# yeniden başlatmak yeter.
set -eu
install -m 755 /deploy/host/abdera-host /usr/local/bin/abdera-host.real
install -d /usr/local/share/abdera
install -m 644 /deploy/instance/compose.yml /usr/local/share/abdera/compose.yml
# SSH oturumu konteynerin ortamını görmez: yerel yollar sarmalayıcıda sabitlenir.
cat > /usr/local/bin/abdera-host <<WRAP
#!/bin/sh
export ABDERA_ROOT="$ABDERA_ROOT" ABDERA_DOCKER_RUN_OPTS="--security-opt label=disable"
exec /usr/local/bin/abdera-host.real "\$@"
WRAP
chmod 755 /usr/local/bin/abdera-host
install -d -m 700 /root/.ssh
echo "command=\"/usr/local/bin/abdera-host\",restrict $(cat /keys/platform_key.pub)" > /root/.ssh/authorized_keys
chmod 600 /root/.ssh/authorized_keys
exec /usr/sbin/sshd -D -e
