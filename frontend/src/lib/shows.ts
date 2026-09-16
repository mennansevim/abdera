// Yıl sonu gösterisi API'leri. Programı öğretmen de okuyabilir (kulis düzeni buna bağlı),
// yalnızca yönetici değiştirebilir - docs/04-permissions.md.
"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, API_BASE_URL } from "./api";

export type ShowStatus = "Draft" | "Live" | "Completed";
export type ShowItemKind = "Performance" | "Intermission" | "Announcement";

export const SHOW_STATUS_LABEL: Record<ShowStatus, string> = {
  Draft: "Taslak",
  Live: "Sahnede",
  Completed: "Tamamlandı",
};

export const SHOW_ITEM_KIND_LABEL: Record<ShowItemKind, string> = {
  Performance: "Sahne sırası",
  Intermission: "Ara",
  Announcement: "Duyuru",
};

export interface ShowSummary {
  id: string;
  title: string;
  venueName: string | null;
  startsAt: string;
  status: ShowStatus;
  itemCount: number;
  performerCount: number;
  totalDurationMinutes: number;
}

export interface ShowItem {
  id: string;
  position: number;
  groupName: string | null;
  kind: ShowItemKind;
  studentId: string | null;
  studentName: string | null;
  hasPhoto: boolean;
  photoVersion: string | null;
  instrumentId: string | null;
  instrumentName: string | null;
  teacherId: string | null;
  teacherName: string | null;
  pieceTitle: string | null;
  composer: string | null;
  durationMinutes: number | null;
  note: string | null;
}

export interface ShowDetail {
  id: string;
  title: string;
  venueName: string | null;
  startsAt: string;
  status: ShowStatus;
  currentItemId: string | null;
  startedAt: string | null;
  endedAt: string | null;
  totalDurationMinutes: number;
  items: ShowItem[];
}

export interface StageItem extends Omit<ShowItem, "instrumentId" | "teacherId"> {
  studentPieces: string[];
  studentPieceIndex: number;
}

export interface StageState {
  showId: string;
  title: string;
  venueName: string | null;
  status: ShowStatus;
  startedAt: string | null;
  startsAt: string;
  current: StageItem | null;
  next: StageItem | null;
  onDeck: StageItem | null;
  totalItems: number;
  completedItems: number;
  totalDurationMinutes: number;
  elapsedMinutes: number;
}

export interface ShowItemInput {
  kind: ShowItemKind;
  groupName?: string | null;
  studentId?: string | null;
  instrumentId?: string | null;
  teacherId?: string | null;
  pieceTitle?: string | null;
  composer?: string | null;
  durationMinutes?: number | null;
  note?: string | null;
}

// Fotoğraf `<img src>` ile doğrudan API'den çekilir; sürüm anahtarı sorgu dizesinde
// taşınır ki fotoğraf değişmediği sürece tarayıcı önbelleği geçerli kalsın (sunucu
// tarafında ETag ile eşleşir - StudentPhotos.cs).
export function studentPhotoUrl(studentId: string, version: string | null) {
  return `${API_BASE_URL}/api/students/${studentId}/photo${version ? `?v=${version}` : ""}`;
}

export function useShows() {
  return useQuery({ queryKey: ["shows"], queryFn: () => api.get<ShowSummary[]>("/api/shows") });
}

export function useShow(showId: string, options?: { enabled?: boolean }) {
  return useQuery({
    queryKey: ["show", showId],
    queryFn: () => api.get<ShowDetail>(`/api/shows/${showId}`),
    enabled: !!showId && (options?.enabled ?? true),
  });
}

// Sahne ekranı tek bir işaretçiyi izler; birden fazla cihaz aynı anı görmek zorunda
// olduğu için düzenli aralıklarla yenilenir (websocket eklemeden - CLAUDE.md'nin
// "bu ölçekte gerçekten gerekli mi" kuralı).
export function useStage(showId: string, options?: { enabled?: boolean; refetchMs?: number }) {
  return useQuery({
    queryKey: ["show-stage", showId],
    queryFn: () => api.get<StageState>(`/api/shows/${showId}/stage`),
    enabled: !!showId && (options?.enabled ?? true),
    refetchInterval: options?.refetchMs ?? 4000,
    refetchIntervalInBackground: true,
  });
}

export function useCreateShow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { title: string; venueName?: string | null; startsAt: string }) =>
      api.post<ShowDetail>("/api/shows", body),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["shows"] }),
  });
}

export function useUpdateShow(showId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: { title: string; venueName?: string | null; startsAt: string }) =>
      api.patch<ShowDetail>(`/api/shows/${showId}`, body),
    onSuccess: (data) => {
      queryClient.setQueryData(["show", showId], data);
      queryClient.invalidateQueries({ queryKey: ["shows"] });
    },
  });
}

export function useDeleteShow() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (showId: string) => api.delete(`/api/shows/${showId}`),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: ["shows"] }),
  });
}

export function useAddShowItem(showId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (body: ShowItemInput) => api.post<ShowDetail>(`/api/shows/${showId}/items`, body),
    onSuccess: (data) => applyShowUpdate(queryClient, showId, data),
  });
}

export function useUpdateShowItem(showId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ itemId, ...body }: ShowItemInput & { itemId: string }) =>
      api.patch<ShowDetail>(`/api/shows/${showId}/items/${itemId}`, body),
    onSuccess: (data) => applyShowUpdate(queryClient, showId, data),
  });
}

export function useDeleteShowItem(showId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (itemId: string) => api.delete<ShowDetail>(`/api/shows/${showId}/items/${itemId}`),
    onSuccess: (data) => applyShowUpdate(queryClient, showId, data),
  });
}

export function useReorderShowItems(showId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (itemIds: string[]) => api.post<ShowDetail>(`/api/shows/${showId}/items/reorder`, { itemIds }),
    onSuccess: (data) => applyShowUpdate(queryClient, showId, data),
  });
}

export type StageCommand = "start" | "advance" | "back" | "finish" | "reopen";

export function useStageControl(showId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ command, itemId }: { command: StageCommand | "goto"; itemId?: string }) =>
      api.post<StageState>(
        command === "goto" ? `/api/shows/${showId}/goto/${itemId}` : `/api/shows/${showId}/${command}`,
      ),
    onSuccess: (data) => {
      // Sunucunun döndüğü durumu doğrudan yazmak, tek tuşla ilerletmede bir sonraki
      // yenileme turunu beklemeden ekranın anında güncellenmesini sağlar.
      queryClient.setQueryData(["show-stage", showId], data);
      queryClient.invalidateQueries({ queryKey: ["show", showId] });
      queryClient.invalidateQueries({ queryKey: ["shows"] });
    },
  });
}

export function useUploadStudentPhoto(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (file: File) => {
      const body = new FormData();
      body.append("file", file);
      const response = await fetch(`${API_BASE_URL}/api/students/${studentId}/photo`, {
        method: "PUT",
        credentials: "include",
        body,
      });
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new Error(problem?.detail ?? problem?.title ?? "Fotoğraf yüklenemedi.");
      }
      return (await response.json()) as { studentId: string; version: string };
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["show"] });
      queryClient.invalidateQueries({ queryKey: ["show-stage"] });
    },
  });
}

export function useDeleteStudentPhoto(studentId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => api.delete(`/api/students/${studentId}/photo`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["show"] });
      queryClient.invalidateQueries({ queryKey: ["show-stage"] });
    },
  });
}

function applyShowUpdate(
  queryClient: ReturnType<typeof useQueryClient>,
  showId: string,
  data: ShowDetail,
) {
  queryClient.setQueryData(["show", showId], data);
  queryClient.invalidateQueries({ queryKey: ["shows"] });
  queryClient.invalidateQueries({ queryKey: ["show-stage", showId] });
}
