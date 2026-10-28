"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiBaseUrl, ApiError } from "./api";

// Ses Defteri (issue #52): öğretmenin ders sonunda aldığı kısa çalış kayıtları. Ses dosyası
// sunucuda veritabanına değil diske yazılır; sıkıştırma burada, tarayıcıda yapılır.

export interface AudioClip {
  id: string;
  studentId: string;
  lessonId: string | null;
  teacherId: string | null;
  teacherName: string | null;
  pieceTitle: string | null;
  contentType: string;
  sizeBytes: number;
  durationSeconds: number;
  starred: boolean;
  sharedWithGuardian: boolean;
  createdAt: string;
}

export interface StudentAudioClips {
  studentId: string;
  consent: boolean;
  consentUpdatedAt: string | null;
  termQuota: number;
  termUsed: number;
  clips: AudioClip[];
}

export interface GuardianAudioClip {
  id: string;
  pieceTitle: string | null;
  teacherName: string | null;
  durationSeconds: number;
  contentType: string;
  createdAt: string;
}

export interface GuardianAudioClips {
  studentId: string;
  consent: boolean;
  clips: GuardianAudioClip[];
}

export interface AudioClipUsage {
  totalBytes: number;
  clipCount: number;
  last30DaysBytes: number;
  notBackedUp: number;
  diskBytes: number;
  diskFiles: number;
}

// Sunucu sınırlarıyla aynı (AudioClip.cs): 60 sn'de kayıt kendiliğinden durur.
export const AUDIO_MAX_SECONDS = 60;
export const AUDIO_MAX_BYTES = 400 * 1024;

// Tek enstrüman için mono yeterli; 32 kbps Opus 60 sn'de ~240 KB eder (ham kaydın ~1/45'i).
// Safari WebM/Opus kaydedemezse AAC'ye düşülür, Opus'tan daha az verimli olduğu için daha yüksek bit hızıyla.
export function pickRecorderFormat(): { mimeType: string; bitsPerSecond: number } | null {
  if (typeof MediaRecorder === "undefined") return null;
  const candidates = [
    { mimeType: "audio/webm;codecs=opus", bitsPerSecond: 32_000 },
    { mimeType: "audio/ogg;codecs=opus", bitsPerSecond: 32_000 },
    { mimeType: "audio/mp4", bitsPerSecond: 48_000 },
  ];
  return candidates.find((candidate) => MediaRecorder.isTypeSupported(candidate.mimeType)) ?? null;
}

export function staffClipUrl(clipId: string) {
  return `${apiBaseUrl()}/api/audio-clips/${clipId}/content`;
}

export function guardianClipUrl(clipId: string) {
  return `${apiBaseUrl()}/api/guardian/me/audio-clips/${clipId}/content`;
}

export function formatBytes(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${Math.round(bytes / 1024)} KB`;
  return `${(bytes / (1024 * 1024)).toLocaleString("tr-TR", { maximumFractionDigits: 1 })} MB`;
}

export function useStudentAudioClips(studentId: string | undefined) {
  return useQuery({
    queryKey: ["audio-clips", studentId],
    queryFn: () => api.get<StudentAudioClips>(`/api/students/${studentId}/audio-clips`),
    enabled: !!studentId,
  });
}

export function useUploadAudioClip(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation<AudioClip, ApiError, { lessonId: string; blob: Blob; durationSeconds: number; pieceTitle?: string }>({
    mutationFn: async ({ lessonId, blob, durationSeconds, pieceTitle }) => {
      const body = new FormData();
      const extension = blob.type.includes("mp4") ? "m4a" : blob.type.includes("ogg") ? "ogg" : "webm";
      body.append("file", blob, `kayit.${extension}`);
      body.append("durationSeconds", String(Math.max(1, Math.min(AUDIO_MAX_SECONDS, Math.round(durationSeconds)))));
      if (pieceTitle?.trim()) body.append("pieceTitle", pieceTitle.trim());
      let response: Response;
      try {
        response = await fetch(`${apiBaseUrl()}/api/lessons/${lessonId}/audio-clips`, { method: "POST", credentials: "include", body });
      } catch {
        throw new ApiError(0, "Sunucuya ulaşılamadı", "Bağlantını kontrol edip kaydı yeniden gönder.");
      }
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        const fieldError = problem?.errors ? Object.values(problem.errors as Record<string, string[]>)[0]?.[0] : undefined;
        throw new ApiError(response.status, problem?.title ?? "Kayıt yüklenemedi", fieldError ?? problem?.detail ?? "Kayıt yüklenemedi.");
      }
      return (await response.json()) as AudioClip;
    },
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["audio-clips", studentId] }),
  });
}

export function useShareAudioClip(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation<AudioClip, ApiError, { clipId: string; shared: boolean }>({
    mutationFn: ({ clipId, shared }) => api.put<AudioClip>(`/api/audio-clips/${clipId}/sharing`, { shared }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["audio-clips", studentId] }),
  });
}

export function useStarAudioClip(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation<AudioClip, ApiError, { clipId: string; starred: boolean }>({
    mutationFn: ({ clipId, starred }) => api.put<AudioClip>(`/api/audio-clips/${clipId}/star`, { starred }),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["audio-clips", studentId] }),
  });
}

export function useDeleteAudioClip(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation<void, ApiError, string>({
    mutationFn: (clipId) => api.delete(`/api/audio-clips/${clipId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["audio-clips", studentId] }),
  });
}

export function useSetAudioConsent(studentId: string, as: "admin" | "guardian") {
  const queryClient = useQueryClient();
  const path = as === "admin" ? `/api/students/${studentId}/audio-consent` : `/api/guardian/me/students/${studentId}/audio-consent`;
  return useMutation<{ studentId: string; consent: boolean; deletedClips: number }, ApiError, boolean>({
    mutationFn: (consent) => api.put(path, { consent }),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: ["audio-clips", studentId] });
      void queryClient.invalidateQueries({ queryKey: ["guardian", "audio-clips", studentId] });
    },
  });
}

export function useGuardianAudioClips(studentId: string | undefined) {
  return useQuery({
    queryKey: ["guardian", "audio-clips", studentId],
    queryFn: () => api.get<GuardianAudioClips>(`/api/guardian/me/students/${studentId}/audio-clips`),
    enabled: !!studentId,
  });
}

export function useAudioClipUsage() {
  return useQuery({
    queryKey: ["audio-clips", "usage"],
    queryFn: () => api.get<AudioClipUsage>("/api/audio-clips/usage"),
  });
}
