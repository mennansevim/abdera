"use client";

// Platform denetim kaydı (platform/api Features/Audit.cs). Salt okunur.
import { useQuery } from "@tanstack/react-query";
import { api } from "./api";

export interface AuditEntry {
  id: string;
  actor: string | null;
  action: string;
  schoolId: string | null;
  slug: string | null;
  beforeJson: string | null;
  afterJson: string | null;
  createdAt: string;
}

export function useAudit(options: { schoolId?: string; take?: number } = {}) {
  const params = new URLSearchParams();
  if (options.schoolId) params.set("schoolId", options.schoolId);
  params.set("take", String(options.take ?? 100));
  return useQuery({
    queryKey: ["audit", options],
    queryFn: () => api.get<AuditEntry[]>(`/api/audit?${params.toString()}`),
  });
}

// Bilinen eylem adlarının Türkçe karşılığı; bilinmeyen ad olduğu gibi gösterilir.
const ACTION_LABEL: Record<string, string> = {
  "operator.bootstrapped": "Operatör hesabı açıldı",
  "operator.login": "Giriş",
  "operator.totp_enabled": "İki adımlı doğrulama açıldı",
  "school.created": "Okul oluşturuldu",
  "school.admin_password_revealed": "Geçici parola görüntülendi",
  "school.upgrade_requested": "Güncelleme istendi",
  "school.flags_changed": "Kanarya/sabitleme değişti",
  "school.modules_changed": "Modüller değişti",
  "school.suspend_requested": "Askıya alma istendi",
  "school.resume_requested": "Devam ettirme istendi",
  "school.destroy_requested": "Silme istendi",
  "school.undestroy_requested": "Silmeyi geri alma istendi",
  "school.purge_requested": "Kalıcı silme istendi",
  "school.adopt_requested": "Sahiplenme (taşıma) istendi",
  "school.cancel_requested": "İptal ve temizlik istendi",
  "school.drill_requested": "Geri yükleme provası istendi",
  "school.activated": "Okul yayına alındı",
  "school.upgraded": "Okul güncellendi",
  "school.suspended": "Okul askıya alındı",
  "school.resumed": "Okul devam ettirildi",
  "school.destroy_cancelled": "Silme geri alındı",
  "school.purged": "Okul kalıcı silindi",
  "school.reconfigured": "Yapılandırma uygulandı",
  "school.provisioning_cancelled": "Kurulum iptal edildi",
  "run.retried": "İşlem yeniden denendi",
  "host.created": "Sunucu eklendi",
  "host.status_changed": "Sunucu durumu değişti",
  "release.registered": "Sürüm kaydedildi",
  "release.withdrawn": "Sürüm geri çekildi",
  "rollout.started": "Dağıtım başladı",
  "rollout.halted": "Dağıtım durduruldu",
  "alert.acknowledged": "Alarm onaylandı",
};

export function auditActionLabel(action: string): string {
  return ACTION_LABEL[action] ?? action;
}
