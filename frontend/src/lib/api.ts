// Backend API ile konuşan tek nokta. Oturum httpOnly cookie ile tutulur (docs/10-decisions.md
// B4) - bu yüzden her istek `credentials: "include"` ile gider, Authorization header yok.

const API_BASE_URL = process.env.NEXT_PUBLIC_API_BASE_URL ?? "http://localhost:8080";

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

async function request<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${API_BASE_URL}${path}`, {
    ...init,
    credentials: "include",
    headers: {
      "Content-Type": "application/json",
      ...init?.headers,
    },
  });

  if (!response.ok) {
    // Backend RFC 7807 ProblemDetails döner (bkz. Shared/GlobalExceptionHandler.cs).
    const problem = await response.json().catch(() => null);
    throw new ApiError(
      response.status,
      problem?.title ?? "Bir hata oluştu",
      problem?.detail,
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
  post: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "POST", body: body ? JSON.stringify(body) : undefined }),
  patch: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PATCH", body: body ? JSON.stringify(body) : undefined }),
  put: <T>(path: string, body?: unknown) =>
    request<T>(path, { method: "PUT", body: body ? JSON.stringify(body) : undefined }),
};

// docs/07-api.md sözleşmesiyle eşleşen minimal tipler - modüller büyüdükçe genişler.
export type UserRole = "Admin" | "Teacher";

export interface Me {
  id: string;
  email: string;
  role: UserRole;
  mustChangePassword: boolean;
}

export interface LoginResponse {
  id: string;
  email: string;
  role: UserRole;
  mustChangePassword: boolean;
}
