"use client";

// Okullardan gelen alarmlar (platform/api Features/Alerts.cs).
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { api } from "./api";

export interface Alert {
  id: string;
  schoolId: string;
  slug: string;
  kind: string;
  level: string;
  detail: string;
  occurredAt: string;
  receivedAt: string;
  acknowledgedAt: string | null;
}

export function useAlerts(openOnly: boolean) {
  return useQuery({
    queryKey: ["alerts", { openOnly }],
    queryFn: () => api.get<Alert[]>(`/api/alerts${openOnly ? "?open=true" : ""}`),
    refetchInterval: 30_000,
  });
}

export function useAcknowledgeAlert() {
  const client = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => api.post<void>(`/api/alerts/${id}/ack`),
    onSettled: () => {
      client.invalidateQueries({ queryKey: ["alerts"] });
      client.invalidateQueries({ queryKey: ["schools"] });
    },
  });
}

// Alarm seviyesi metin olarak gelir (okulun AlertWebhookNotifier'ı: "Critical", "Warning"...).
export function alertTone(level: string): "danger" | "warning" | "muted" {
  const value = level.toLowerCase();
  if (value.includes("crit") || value.includes("error") || value.includes("unhealthy")) return "danger";
  if (value.includes("warn") || value.includes("degraded")) return "warning";
  return "muted";
}

// Platformun kendi ürettiği alarm türleri (CertificateWatcher). Okuldan gelen türler olduğu gibi gösterilir.
const ALERT_KIND_LABEL: Record<string, string> = {
  tls_expiring: "Sertifika bitiyor",
  tls_invalid: "Sertifika geçersiz",
};

export function alertKindLabel(kind: string): string {
  return ALERT_KIND_LABEL[kind] ?? kind;
}
