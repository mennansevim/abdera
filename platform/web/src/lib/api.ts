// Platform API ile konuşan tek nokta. Oturum httpOnly `abdera_platform` çereziyle tutulur -
// her istek `credentials: "include"` ile gider, Authorization header yok.
//
// Taban adres: NEXT_PUBLIC_API_BASE_URL verilmişse o; yoksa boş (aynı origin). Platform API'si
// CORS açmadığı ve çerez SameSite=Strict olduğu için panel API'ye aynı origin'den konuşmalı:
// yayında Caddy `/api`'yi API'ye yönlendirir, yerel geliştirmede next.config.ts'teki rewrite
// `/api`'yi http://localhost:8090'a aktarır.
export function apiBaseUrl(): string {
  return (process.env.NEXT_PUBLIC_API_BASE_URL ?? "").replace(/\/$/, "");
}

export class ApiError extends Error {
  constructor(
    public status: number,
    public title: string,
    public detail?: string,
    public errors?: Record<string, string[]>,
  ) {
    super(detail ?? title);
  }
}

// Gövdesiz hata yanıtları (rate limiter, proxy, 404) için okunabilir karşılıklar.
const FALLBACK_MESSAGES: Record<number, { title: string; detail: string }> = {
  401: { title: "Oturum gerekli", detail: "Oturumun sona ermiş olabilir; tekrar giriş yap." },
  403: { title: "Doğrulama gerekli", detail: "Bu işlem için iki adımlı doğrulanmış oturum gerekiyor." },
  404: { title: "Bulunamadı", detail: "Aradığın kayıt bulunamadı; sayfayı yenilemeyi dene." },
  409: { title: "Çakışma", detail: "Bu işlem mevcut durumla çakışıyor." },
  429: { title: "Çok fazla deneme", detail: "Güvenlik için bir süre beklemen gerekiyor." },
  500: { title: "Sunucu hatası", detail: "Beklenmeyen bir hata oldu; birazdan tekrar dene." },
  502: { title: "Sunucuya ulaşılamadı", detail: "Platform API'si şu an yanıt vermiyor." },
  503: { title: "Sunucu meşgul", detail: "Platform API'si şu an yanıt vermiyor." },
};

// Bu uçlardaki 401/400 oturum kaybı değil, girilen parolanın/kodun yanlış olduğu anlamına gelir.
const CREDENTIAL_CHECK_PATHS = new Set(["/api/auth/login", "/api/auth/totp/confirm"]);

// Oturum düşünce (süre doldu, TOTP değişti) her istek 401 döner; 403 ise oturumun TOTP ile
// doğrulanmadığını söyler. Panel kabuğu dinler ve kullanıcıyı uygun ekrana yollar.
type SessionEvent = "lost" | "unverified";
const sessionListeners = new Set<(event: SessionEvent) => void>();

export function onSessionEvent(listener: (event: SessionEvent) => void): () => void {
  sessionListeners.add(listener);
  return () => sessionListeners.delete(listener);
}

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  let response: Response;
  try {
    response = await fetch(`${apiBaseUrl()}${path}`, {
      ...init,
      credentials: "include",
      headers: {
        "Content-Type": "application/json",
        ...init?.headers,
      },
    });
  } catch {
    throw new ApiError(0, "Sunucuya ulaşılamadı", "Bağlantını kontrol et; sorun sürerse platform API'sinin çalıştığından emin ol.");
  }

  if (!CREDENTIAL_CHECK_PATHS.has(path) && !path.startsWith("/api/auth/")) {
    if (response.status === 401) sessionListeners.forEach((listener) => listener("lost"));
    if (response.status === 403) sessionListeners.forEach((listener) => listener("unverified"));
  }

  if (!response.ok) {
    // Platform API'si hataları {title,status,detail,errors?} olarak döner (Program.cs).
    const problem = await response.json().catch(() => null);
    const fallback = FALLBACK_MESSAGES[response.status];
    throw new ApiError(
      response.status,
      problem?.title ?? fallback?.title ?? "Bir hata oluştu",
      problem?.detail ?? fallback?.detail,
      problem?.errors ?? undefined,
    );
  }

  // 202/204 gibi boş gövdeli yanıtlarda response.json() patlar.
  const text = await response.text();
  if (!text) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body: body === undefined ? undefined : JSON.stringify(body) }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PUT", body: body === undefined ? undefined : JSON.stringify(body) }),
};

// Hata nesnesinden ekranda gösterilecek tek satır. Doğrulama hatalarında alan mesajları eklenir.
export function errorMessage(error: unknown): string {
  if (error instanceof ApiError) {
    const fieldMessages = error.errors ? Object.values(error.errors).flat() : [];
    if (fieldMessages.length > 0) return fieldMessages.join(" ");
    return error.detail ?? error.title;
  }
  return "Bir hata oluştu.";
}
