"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiBaseUrl, ApiError } from "./api";

type ScoreFileSummary = { entryId: string; pageCount: number | null; sizeBytes: number; version: string };

// Nota PDF'leri yalnızca okul içinde kullanılır: API bu uçları yalnızca giriş yapmış
// öğretmen/yöneticiye açar. Herkese açık /kutuphane sayfası bu hook'ları hiç etkinleştirmez.
export function useScoreFiles(enabled: boolean) {
  return useQuery<Map<string, ScoreFileSummary>, ApiError>({
    queryKey: ["library", "score-files"],
    queryFn: async () => new Map((await api.get<ScoreFileSummary[]>("/api/library/score-files")).map((file) => [file.entryId, file])),
    enabled,
    staleTime: 5 * 60_000,
  });
}

// Oturum çerezi bu adrese de gider (canlıda aynı origin, yerelde aynı site); tarayıcı PDF'i
// kendi görüntüleyicisinde açar. Sürüm parametresi dosya değişince önbelleği atlatır.
export function scoreFileUrl(entryId: string, version: string) {
  return `${apiBaseUrl()}/api/library/score-files/${encodeURIComponent(entryId)}?v=${version}`;
}

// Kitap eserinin PDF'ini yalnızca yönetici, eklenen eserin PDF'ini onu ekleyen öğretmen veya
// yönetici yükleyebilir; sunucu kuralı uygular. 4 MB sınırı Vercel'in 4,5 MB gövde sınırından gelir.
export const SCORE_FILE_MAX_BYTES = 4 * 1024 * 1024;

export function useUploadScoreFile() {
  const queryClient = useQueryClient();
  return useMutation<ScoreFileSummary, ApiError, { entryId: string; file: File }>({
    mutationFn: async ({ entryId, file }) => {
      const body = new FormData();
      body.append("file", file);
      const response = await fetch(`${apiBaseUrl()}/api/library/score-files/${encodeURIComponent(entryId)}`, {
        method: "PUT",
        credentials: "include",
        body,
      });
      if (!response.ok) throw await toApiError(response, "PDF yüklenemedi.");
      return (await response.json()) as ScoreFileSummary;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["library", "score-files"] }),
  });
}

export type LibraryPiece = {
  id: string;
  entryId: string;
  title: string;
  composer: string;
  instrument: string;
  category: string;
  level: number | null;
  notes: string | null;
  canEdit: boolean;
  createdAt: string;
};

export type LibraryPieceInput = { title: string; composer: string; instrument: string; category: string; level: number | null; notes: string };

export function useLibraryPieces(enabled: boolean) {
  return useQuery<LibraryPiece[], ApiError>({
    queryKey: ["library", "pieces"],
    queryFn: () => api.get<LibraryPiece[]>("/api/library/pieces"),
    enabled,
  });
}

export function useCreateLibraryPiece() {
  const queryClient = useQueryClient();
  return useMutation<LibraryPiece, ApiError, LibraryPieceInput>({
    mutationFn: (input) => api.post<LibraryPiece>("/api/library/pieces", input),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["library", "pieces"] }),
  });
}

export function useDeleteLibraryPiece() {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, string>({
    mutationFn: (id) => api.delete(`/api/library/pieces/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["library", "pieces"] });
      queryClient.invalidateQueries({ queryKey: ["library", "score-files"] });
    },
  });
}

export type LibrarySuggestion = {
  id: string;
  studentId: string;
  entryId: string;
  title: string;
  composer: string;
  note: string | null;
  suggestedByName: string;
  canRemove: boolean;
  createdAt: string;
};

export function useStudentSuggestions(studentId: string) {
  return useQuery<LibrarySuggestion[], ApiError>({
    queryKey: ["library", "suggestions", studentId],
    queryFn: () => api.get<LibrarySuggestion[]>(`/api/students/${studentId}/library-suggestions`),
    enabled: !!studentId,
  });
}

export function useSuggestToStudent() {
  const queryClient = useQueryClient();
  return useMutation<LibrarySuggestion, ApiError, { studentId: string; entryId: string; title: string; composer: string; note: string }>({
    mutationFn: ({ studentId, ...body }) => api.post<LibrarySuggestion>(`/api/students/${studentId}/library-suggestions`, body),
    onSuccess: (_, { studentId }) => queryClient.invalidateQueries({ queryKey: ["library", "suggestions", studentId] }),
  });
}

export function useRemoveSuggestion(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, string>({
    mutationFn: (id) => api.delete(`/api/library-suggestions/${id}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["library", "suggestions", studentId] }),
  });
}

// FormData yüklemesi api.ts'teki JSON istemcisini kullanamaz; hata gövdesini aynı biçime çevirir.
async function toApiError(response: Response, fallback: string) {
  const problem = await response.json().catch(() => null);
  const fieldError = problem?.errors ? Object.values(problem.errors as Record<string, string[]>)[0]?.[0] : undefined;
  return new ApiError(response.status, problem?.title ?? fallback, fieldError ?? problem?.detail ?? fallback);
}

// Doğrulama hatalarında ProblemDetails.errors alanındaki ilk mesajı kullanıcıya gösterir.
export function errorMessage(error: unknown, fallback: string) {
  if (!(error instanceof ApiError)) return fallback;
  const fieldError = error.errors ? Object.values(error.errors)[0]?.[0] : undefined;
  return fieldError ?? error.detail ?? error.title ?? fallback;
}
