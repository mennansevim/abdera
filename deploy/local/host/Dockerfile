# Yerel test ortamının "okul sunucusu": Hetzner'deki sunucunun yerine geçer. Platform buna gerçek SSH ile
# bağlanır; authorized_keys yalnızca abdera-host'u çalıştırır (setup-host.sh ile aynı kısıt). Docker,
# VM'in soketi üzerinden kullanılır; okullar VM'de bu konteynerin kardeşi olarak çalışır.
FROM docker.io/library/docker:27.3.1-cli
RUN apk add --no-cache --upgrade bash util-linux coreutils openssl gzip curl openssh-server openssh-client-default
# Parolasız ama kilitsiz root (yalnızca anahtarla giriş; parola girişi kapalı).
RUN ssh-keygen -A \
 && sed -i 's/^root:[!*]*:/root:*:/' /etc/shadow \
 && sed -i 's/^#\?PasswordAuthentication.*/PasswordAuthentication no/; s/^#\?PermitRootLogin.*/PermitRootLogin prohibit-password/' /etc/ssh/sshd_config
COPY entrypoint.sh /entrypoint.sh
EXPOSE 22
ENTRYPOINT ["/entrypoint.sh"]
