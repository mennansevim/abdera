"use client";

// Giriş ekranında rol seçilince o rolün bilgilerini hazır getirir (mobil istek: "Yönetici
// seçilince yöneticinin, Öğretmen seçilince öğretmenin e-postası ve şifresi gelsin").
//
// Şifre BİZİM depomuza hiç yazılmaz. Yalnızca rol başına son başarılı girişin e-postası
// localStorage'da tutulur; şifreyi tarayıcının kendi şifre yöneticisi saklar ve verir:
// - Chrome/Android (Credential Management API): başarılı girişten sonra kimlik tarayıcıya
//   kaydedilir, rol seçilince geri istenir. Birden fazla kayıt varsa tarayıcı hesap seçtirir.
// - iOS Safari bu API'yi desteklemez; programla doldurmaya izin vermez. Orada e-posta
//   önceden gelir, şifre alanına dokununca Anahtar Zinciri o e-postanın şifresini önerir.
type StaffRole = "Admin" | "Teacher";

const EMAIL_KEY_PREFIX = "abdera.login.email:";

export function readSavedEmail(role: StaffRole): string {
  try {
    return window.localStorage.getItem(EMAIL_KEY_PREFIX + role) ?? "";
  } catch {
    return "";
  }
}

export function rememberLogin(role: StaffRole, email: string, password: string) {
  try {
    window.localStorage.setItem(EMAIL_KEY_PREFIX + role, email);
  } catch {
    // Gizli mod/depolama kapalı: bir sonraki girişte e-posta yine elle yazılır.
  }

  const PasswordCredentialCtor = passwordCredentialCtor();
  if (!PasswordCredentialCtor) return;
  const name = role === "Admin" ? "Abdera · Yönetici" : "Abdera · Öğretmen";
  navigator.credentials.store(new PasswordCredentialCtor({ id: email, password, name })).catch(() => {});
}

// Seçilen rol için kayıtlı kimliği tarayıcıdan ister. Tarayıcının verdiği hesap başka bir
// rolde hatırlanan e-postaysa `role` o rolü gösterir - giriş ekranı seçimi ona çeker ki
// sunucu "rol uyuşmuyor" diye reddetmesin.
export async function requestSavedLogin(role: StaffRole): Promise<{ email: string; password: string; role: StaffRole } | null> {
  if (!passwordCredentialCtor()) return null;
  try {
    const credential = await navigator.credentials.get({ password: true, mediation: "optional" } as CredentialRequestOptions);
    if (!credential || credential.type !== "password") return null;
    const { id, password } = credential as Credential & { password?: string };
    if (!password) return null;
    const otherRole: StaffRole = role === "Admin" ? "Teacher" : "Admin";
    const matchesOther = readSavedEmail(otherRole) === id && readSavedEmail(role) !== id;
    return { email: id, password, role: matchesOther ? otherRole : role };
  } catch {
    return null;
  }
}

type PasswordCredentialConstructor = new (data: { id: string; password: string; name?: string }) => Credential;

function passwordCredentialCtor(): PasswordCredentialConstructor | null {
  if (typeof window === "undefined" || !("credentials" in navigator)) return null;
  return (window as unknown as { PasswordCredential?: PasswordCredentialConstructor }).PasswordCredential ?? null;
}
