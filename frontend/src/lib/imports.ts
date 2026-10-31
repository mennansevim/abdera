"use client";

import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api, apiBaseUrl, ApiError } from "./api";

// Excel'den içe aktarma (docs/10-decisions.md AF). Aynı dosya önce önizlemeye, onaydan sonra
// içe aktarmaya gönderilir; sunucu ikinci seferde dosyayı baştan doğrular.
export type ImportKind = "students" | "payments";
export type ImportRowStatus = "Ready" | "Imported" | "Skipped" | "Error";
export type ImportRow = { row: number; label: string; status: ImportRowStatus; message: string };
export type ImportReport = {
  committed: boolean;
  total: number;
  ready: number;
  imported: number;
  skipped: number;
  errors: number;
  rows: ImportRow[];
};

export function importTemplateUrl(kind: ImportKind) {
  return `${apiBaseUrl()}/api/imports/${kind}/template`;
}

export function useSpreadsheetImport(kind: ImportKind) {
  const queryClient = useQueryClient();
  return useMutation<ImportReport, ApiError, { file: File; commit: boolean }>({
    mutationFn: async ({ file, commit }) => {
      const body = new FormData();
      body.append("file", file);
      let response: Response;
      try {
        response = await fetch(`${apiBaseUrl()}/api/imports/${kind}${commit ? "" : "/preview"}`, {
          method: "POST",
          credentials: "include",
          body,
        });
      } catch {
        throw new ApiError(0, "Sunucuya ulaşılamadı", "İnternet bağlantını kontrol et ve tekrar dene.");
      }
      if (!response.ok) {
        const problem = await response.json().catch(() => null);
        throw new ApiError(response.status, problem?.title ?? "Dosya işlenemedi", problem?.detail, problem?.errors);
      }
      return (await response.json()) as ImportReport;
    },
    // İçe aktarma öğrenci listesini, aidatları ve gelir ekranlarını değiştirir.
    onSuccess: (report) => {
      if (report.committed) void queryClient.invalidateQueries();
    },
  });
}

// Tüm verinin Excel dökümü (docs/10-decisions.md AJ). Sunucu arka planda üretir; liste döküm
// sürerken kısa aralıkla yoklanır. Yalnızca son üç döküm tutulur.
export type DataExportStatus = "Pending" | "Running" | "Completed" | "Failed";
export type DataExport = {
  id: string;
  status: DataExportStatus;
  fileName: string | null;
  sizeBytes: number | null;
  errorMessage: string | null;
  createdAt: string;
  completedAt: string | null;
};

const dataExportsKey = ["data-exports"] as const;

export function isExportInProgress(item: DataExport) {
  return item.status === "Pending" || item.status === "Running";
}

export function useDataExports() {
  return useQuery({
    queryKey: dataExportsKey,
    queryFn: () => api.get<DataExport[]>("/api/data-exports"),
    refetchInterval: (query) => (query.state.data?.some(isExportInProgress) ? 2_000 : false),
  });
}

export function useRequestDataExport() {
  const queryClient = useQueryClient();
  return useMutation<DataExport, ApiError>({
    mutationFn: () => api.post<DataExport>("/api/data-exports"),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: dataExportsKey }),
  });
}

export function dataExportFileUrl(id: string) {
  return `${apiBaseUrl()}/api/data-exports/${id}/file`;
}
