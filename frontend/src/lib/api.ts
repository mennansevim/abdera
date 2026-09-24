// Backend API ile konuşan tek nokta. Oturum httpOnly cookie ile tutulur (docs/10-decisions.md
// B4) - bu yüzden her istek `credentials: "include"` ile gider, Authorization header yok.

// Yayında frontend ve API aynı Vercel domain'inde /api üzerinden sunulur; boş taban URL
// tarayıcının mevcut origin'ini kullanır. Yerel geliştirmede ayrı API portu korunur.
const configuredApiBaseUrl = process.env.NEXT_PUBLIC_API_BASE_URL;
const configuredApiIsLoopback = configuredApiBaseUrl
  ? /^https?:\/\/(localhost|127\.0\.0\.1)(:\d+)?\/?$/i.test(configuredApiBaseUrl)
  : false;

// Docker geliştirme varsayılanı localhost:8080'dir. Aynı imaj production Caddy profiliyle
// oluşturulduğunda bu adres ziyaretçinin KENDİ cihazını gösterir ve bütün API çağrıları
// sessizce bozulur. Bu yüzden production'da loopback değeri aynı-origin `/api`'ye çevrilir -
// ama YALNIZCA sayfa gerçek bir alan adından açılmışsa. Sayfanın kendisi de localhost'tan
// açılıyorsa (profilsiz `docker compose up`, CI e2e smoke) web ve API aynı makinededir,
// localhost:8080 doğru adrestir ve 3000 portunda `/api` diye bir şey yoktur. Karar önceden
// derleme anında veriliyordu: CI'da ve yerel compose'da bütün istekler Next.js'e gidip 404
// dönüyor, giriş hiç tamamlanmıyordu (gerçek bir CI hatası olarak bulundu). Açıkça verilen
// gerçek bir uzak API origin'i her durumda korunur.
const LOOPBACK_HOSTNAME = /^(localhost|127\.0\.0\.1|\[::1\])$/i;

export function apiBaseUrl(): string {
  if (configuredApiBaseUrl === undefined) {
    return process.env.NODE_ENV === "production" ? "" : "http://localhost:8080";
  }
  if (process.env.NODE_ENV === "production" && configuredApiIsLoopback) {
    const pageIsLoopback = typeof window !== "undefined" && LOOPBACK_HOSTNAME.test(window.location.hostname);
    return pageIsLoopback ? configuredApiBaseUrl.replace(/\/$/, "") : "";
  }
  return configuredApiBaseUrl;
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

// Gövdesiz hata yanıtları için okunabilir karşılıklar. Backend'in çoğu hatası RFC 7807
// ProblemDetails taşır, ama altyapı katmanından gelen bazı yanıtlar (rate limiter, proxy,
// 404) boş gövdeyle döner - o durumda ekranda yalnızca "Bir hata oluştu" görünüyordu ve
// kullanıcı ne yapacağını bilmiyordu.
const FALLBACK_MESSAGES: Record<number, { title: string; detail: string }> = {
  401: { title: "Oturum gerekli", detail: "Oturumun sona ermiş olabilir; tekrar giriş yap." },
  403: { title: "Yetki yok", detail: "Bu işlem için yetkin yok." },
  404: { title: "Bulunamadı", detail: "Aradığın kayıt bulunamadı; sayfayı yenilemeyi dene." },
  409: { title: "Çakışma", detail: "Bu işlem mevcut kayıtlarla çakışıyor." },
  429: { title: "Çok fazla deneme", detail: "Güvenlik için bir süre beklemen gerekiyor." },
  500: { title: "Sunucu hatası", detail: "Beklenmeyen bir hata oldu; birazdan tekrar dene." },
  502: { title: "Sunucuya ulaşılamadı", detail: "Sunucu şu an yanıt vermiyor; birazdan tekrar dene." },
  503: { title: "Sunucu meşgul", detail: "Sunucu şu an yanıt vermiyor; birazdan tekrar dene." },
};

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
    // Ağ hatası: fetch reddedilir ve çağıran ekranlar `err instanceof ApiError` kontrolü
    // yaptığı için ham TypeError "Bir hata oluştu"ya düşüyordu.
    throw new ApiError(0, "Sunucuya ulaşılamadı", "İnternet bağlantını kontrol et; sorun sürerse sunucunun çalıştığından emin ol.");
  }

  if (!response.ok) {
    // Backend RFC 7807 ProblemDetails döner (bkz. Shared/GlobalExceptionHandler.cs).
    const problem = await response.json().catch(() => null);
    const fallback = FALLBACK_MESSAGES[response.status];
    throw new ApiError(
      response.status,
      problem?.title ?? fallback?.title ?? "Bir hata oluştu",
      problem?.detail ?? fallback?.detail,
      problem?.errors,
    );
  }

  // 204 veya içerik uzunluğu 0 olan (boş gövdeli) başarılı yanıtlarda response.json()
  // "Unexpected end of JSON input" ile patlar - boş gövdeyi güvenle undefined'a çeviriyoruz.
  const text = await response.text();
  if (!text) {
    return undefined as T;
  }

  return JSON.parse(text) as T;
}

export const api = {
  get: <T>(path: string) => request<T>(path),
  post: <T>(path: string, body?: unknown, options?: { headers?: HeadersInit }) =>
    request<T>(path, { method: "POST", body: body ? JSON.stringify(body) : undefined, headers: options?.headers }),
  patch: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PATCH", body: body ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PUT", body: body ? JSON.stringify(body) : undefined }),
  delete: <T>(path: string) => request<T>(path, { method: "DELETE" }),
};

// docs/07-api.md sözleşmesiyle eşleşen minimal tipler - modüller büyüdükçe genişler.
export type UserRole = "Admin" | "Teacher";

export interface Me {
  id: string;
  email: string;
  role: UserRole;
  mustChangePassword: boolean;
  // Teacher oturumunda kendisine ait teacher kaydının id'si (Admin'de null). Öğretmen
  // kendi öğrencisini ekleyebildiği için istemcinin bu id'ye ihtiyacı var
  // (POST /api/teachers/{teacherId}/students).
  teacherId: string | null;
  // Teacher oturumunda kendi çaldığı enstrümanlar (Admin'de her zaman boş dizi) - Takvim
  // ekranındaki enstrüman filtresini yalnızca kendi branşıyla sınırlamak için.
  instrumentIds: string[];
}

export interface LoginResponse {
  id: string;
  email: string;
  role: UserRole;
  mustChangePassword: boolean;
}
