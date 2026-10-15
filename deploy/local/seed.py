#!/usr/bin/env python3
"""Yerel test ortamını platforma tanıtır: operatörün iki adımlı doğrulaması, sürüm kaydı ve sunucu.

  python3 deploy/local/seed.py                       # .out/release-*.json sürümlerini ve sunucuyu kaydeder
  python3 deploy/local/seed.py totp                  # panele giriş için o anki doğrulama kodu
  python3 deploy/local/seed.py school <kısaltma> "<ad>" [modül ...]   # sihirbazın gönderdiği istekle okul aç
  python3 deploy/local/seed.py reveal <kısaltma>     # yöneticinin geçici parolası -> .out/<kısaltma>-admin.env (denetime yazılır)

TOTP gizli anahtarı .out/operator_totp'de durur (repoya girmez). Tekrar çalıştırmak güvenlidir.
"""
import base64, glob, hashlib, hmac, http.cookiejar, json, os, struct, sys, time, urllib.error, urllib.request

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(HERE, ".out")
BASE = os.environ.get("PLATFORM_URL", "http://localhost:8090")


def env_file(name):
    values = {}
    for line in open(os.path.join(OUT, name)):
        if "=" in line:
            key, value = line.rstrip("\n").split("=", 1)
            values[key] = value
    return values


def totp(secret, at=None):
    key = base64.b32decode(secret + "=" * (-len(secret) % 8))
    digest = hmac.new(key, struct.pack(">Q", int((at or time.time()) // 30)), hashlib.sha1).digest()
    offset = digest[-1] & 15
    return "%06d" % ((struct.unpack(">I", digest[offset:offset + 4])[0] & 0x7FFFFFFF) % 1_000_000)


jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))


def call(method, path, body=None):
    request = urllib.request.Request(BASE + path, data=None if body is None else json.dumps(body).encode(),
                                     method=method, headers={"Content-Type": "application/json"})
    try:
        with opener.open(request) as response:
            raw = response.read()
            return response.status, json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        return error.code, error.read().decode()


def login():
    operator = env_file("operator.env")
    secret_path = os.path.join(OUT, "operator_totp")
    secret = open(secret_path).read().strip() if os.path.exists(secret_path) else None
    status, body = call("POST", "/api/auth/login", {"email": operator["OPERATOR_EMAIL"], "password": operator["OPERATOR_PASSWORD"],
                                                     "totpCode": totp(secret) if secret else None})
    if status == 401 and secret:
        # Aynı doğrulama kodu iki kez kabul edilmez: bir sonraki 30 sn'lik pencereyi bekle.
        time.sleep(30 - int(time.time()) % 30 + 1)
        status, body = call("POST", "/api/auth/login", {"email": operator["OPERATOR_EMAIL"], "password": operator["OPERATOR_PASSWORD"],
                                                         "totpCode": totp(secret)})
    if status != 200:
        sys.exit(f"giriş başarısız: {status} {body}")
    if not secret:
        _, setup = call("POST", "/api/auth/totp/setup")
        secret = setup["secret"]
        with open(secret_path, "w") as handle:
            handle.write(secret)
        os.chmod(secret_path, 0o600)
        status, body = call("POST", "/api/auth/totp/confirm", {"code": totp(secret)})
        if status >= 300:
            sys.exit(f"TOTP doğrulanamadı: {status} {body}")
        print("İki adımlı doğrulama kuruldu.")


def create_school(slug, name, modules):
    _, releases = call("GET", "/api/releases")
    version = sorted((r["version"] for r in releases if not r.get("withdrawnAt")), key=lambda v: [int(p) for p in v.split(".")])[-1]
    status, body = call("POST", "/api/schools", {
        "slug": slug, "displayName": name, "shortName": name.split(" Müzik")[0], "primaryColor": "#1d6fa5",
        "logoBase64": None, "legalName": None, "contactEmail": None, "customDomain": None, "version": version,
        "modules": modules, "admin": {"email": f"mudur@{slug}.test", "displayName": "Okul Müdürü"},
        "policy": {"groupLessonSharePercent": 40, "maxWeeklyLessons": 4, "makeupNoticeHours": 24, "makeupCreditValidDays": 60,
                   "excusedMakeupWindowDays": 21, "schoolDayStart": "09:00:00", "schoolDayEnd": "21:00:00"},
        "billing": {"multiCourseDiscountPercent": 5, "siblingDiscountPercent": 5, "dueDayOfMonth": 1},
        "tariffs": [{"courseKind": "Individual", "lessonsPerMonth": 4, "monthlyAmount": 3500, "currency": "TRY", "effectiveFrom": "2026-10-01"},
                    {"courseKind": "Group", "lessonsPerMonth": 4, "monthlyAmount": 2000, "currency": "TRY", "effectiveFrom": "2026-10-01"}],
        "integrations": {}, "isCanary": False})
    print(f"okul {slug}: {'kuruluyor (' + body['id'] + ')' if status < 300 else body}")


def reveal(slug):
    _, schools = call("GET", "/api/schools")
    school = next(s for s in schools if s["slug"] == slug)
    status, body = call("POST", f"/api/schools/{school['id']}/reveal-admin-password")
    if status >= 300:
        sys.exit(f"parola alınamadı: {status} {body}")
    _, detail = call("GET", f"/api/schools/{school['id']}")
    path = os.path.join(OUT, f"{slug}-admin.env")
    with open(path, "w") as handle:
        handle.write(f"ADMIN_EMAIL={detail['adminEmail']}\nADMIN_PASSWORD={body['temporaryPassword']}\n")
    os.chmod(path, 0o600)
    print(f"{slug}: {path}")


def main():
    if sys.argv[1:2] == ["reveal"]:
        login()
        for slug in sys.argv[2:]:
            reveal(slug)
        return
    if sys.argv[1:2] == ["school"]:
        login()
        create_school(sys.argv[2], sys.argv[3], sys.argv[4:])
        return
    if sys.argv[1:] == ["totp"]:
        secret = open(os.path.join(OUT, "operator_totp")).read().strip()
        print(f"{totp(secret)}  ({30 - int(time.time()) % 30} sn geçerli)")
        return
    login()
    for path in sorted(glob.glob(os.path.join(OUT, "release-*.json"))):
        release = json.load(open(path))
        status, body = call("POST", "/api/releases", release)
        print(f"sürüm {release['version']}: {'kaydedildi' if status < 300 else 'zaten var' if status == 409 else body}")
    _, hosts = call("GET", "/api/hosts")
    if not any(host["name"] == "yerel-1" for host in hosts):
        fingerprint = open(os.path.join(OUT, "host_fingerprint")).read().strip()
        status, body = call("POST", "/api/hosts", {"name": "yerel-1", "address": "localhost", "sshPort": 2222, "sshUser": "root",
                                                   "hostKeyFingerprint": fingerprint, "publicIp": "127.0.0.1", "totalMemoryMb": 3892})
        print("sunucu yerel-1:", "eklendi" if status < 300 else body)
    else:
        print("sunucu yerel-1: zaten var")


main()
