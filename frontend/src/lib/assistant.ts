// Uygulama içi asistan (docs/10-decisions.md AE, AE9). Sohbet dil modeliyle (DeepSeek) sunucuda
// yürür (POST /api/assistant/chat); kartların verisi ve yazma işlemleri mevcut ekranların
// kullandığı uçlardan geçer. Bu dosya istek tipleri ile kartların ortak hesaplarını tutar.
"use client";

import { useMutation } from "@tanstack/react-query";
import { api, type UserRole } from "./api";
import type { BillingDue, CourseKind } from "./billing";

// İlk etapta yalnız yönetici kullanır (docs/10-decisions.md AE7): öğretmenlere bir hafta sonra
// açılacak. Sunucu ucu öğretmen kapsamını zaten destekliyor; kapı yalnızca arayüzde.
export function canUseAssistant(role: UserRole | undefined) {
  return role === "Admin";
}

export type RescheduleScope = "Unspecified" | "Once" | "Recurring";

export interface AssistantCourse {
  enrollmentId: string;
  instrumentName: string;
  courseKind: CourseKind;
  teacherName: string;
}

export interface AssistantStudent {
  studentId: string;
  fullName: string;
  status: "Active" | "Inactive";
  courses: AssistantCourse[];
}

export interface AssistantMove {
  // YYYY-MM-DD ve HH:mm:ss; okulun yerel takvimine göre çözülmüş.
  sourceDate: string | null;
  sourceTime: string | null;
  targetDate: string | null;
  targetTime: string | null;
  scope: RescheduleScope;
  // Dersin kendi saatine göre dakika: "yarım saat ileri al" → 30, "bir saat erkene" → -60.
  shiftMinutes: number | null;
  // Hedef yalnızca gün adıysa ("cumaya al"): tek seferlik taşımada taşınan dersin haftasındaki
  // o gün kastedilir; targetDate bugünden sonraki ilk o gündür. "haftaya cuma"da targetNextWeek.
  targetWeekday: string | null;
  targetNextWeek: boolean;
}

export type AssistantCardKind = "progress" | "lessons" | "makeup" | "reschedule" | "dues" | "navigate";

// Modelin açtığı kart: veri kartlar tarafından mevcut uçlardan çekilir, yazma işlemleri kartta onayla.
export interface AssistantCardData {
  kind: AssistantCardKind;
  student: AssistantStudent | null;
  move: AssistantMove | null;
  navigation: { key: string; label: string; path: string } | null;
}

// Sohbet geçmişi sunucudan MASKELİ döner (kişi adları kodlu) ve her mesajla aynen geri gönderilir;
// sunucu sohbeti saklamaz (docs/10-decisions.md AE9).
export interface ChatHistoryItem {
  role: "user" | "assistant";
  content: string;
}

export interface AssistantChatResponse {
  reply: string;
  cards: AssistantCardData[];
  history: ChatHistoryItem[];
}

export function useAssistantChat() {
  return useMutation({
    mutationFn: (body: { message: string; history: ChatHistoryItem[]; studentId?: string | null }) =>
      api.post<AssistantChatResponse>("/api/assistant/chat", body),
  });
}

export function initials(fullName: string) {
  return fullName.split(" ").filter(Boolean).slice(0, 2).map((part) => part[0]?.toLocaleUpperCase("tr-TR")).join("");
}

// Tarayıcının yerel günü (takvim ve telafi ekranları da yerel saatle çalışır).
export function localDateKey(date: Date) {
  const pad = (value: number) => String(value).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

export function hhmm(date: Date) {
  return date.toLocaleTimeString("tr-TR", { hour: "2-digit", minute: "2-digit" });
}

export function dayLabel(date: Date) {
  return date.toLocaleDateString("tr-TR", { day: "numeric", month: "long", weekday: "long" });
}

// Onay anındaki son denetim için (kart uzun süre açık kalmış olabilir).
export function hasPassed(date: Date) {
  return date.getTime() <= Date.now();
}

export function addDays(date: Date, days: number) {
  const next = new Date(date);
  next.setDate(next.getDate() + days);
  return next;
}

// "Aidat günü gelenler": ödenmemiş (ya da kısmen ödenmiş) ve vadesi geçmiş ya da önümüzdeki
// 7 gün içinde olan aidatlar. Durum dili Aidatlar ekranıyla aynı (AD18): vadesi geçmişse
// "Ödenmedi", gelmemişse "Yaklaşıyor".
export const DUE_WINDOW_DAYS = 7;
export type DueBucket = "overdue" | "today" | "week";

export interface DueLine {
  due: BillingDue;
  bucket: DueBucket;
  remaining: number;
}

export function openDueLines(dues: BillingDue[], today: Date, studentId?: string): DueLine[] {
  const todayKey = localDateKey(today);
  const lastKey = localDateKey(addDays(today, DUE_WINDOW_DAYS));
  return dues
    .filter((due) => due.status === "Unpaid" || due.status === "Partial" || due.status === "Overdue")
    .filter((due) => (studentId ? due.studentId === studentId : due.dueDate <= lastKey))
    .map((due) => ({
      due,
      bucket: (due.dueDate < todayKey ? "overdue" : due.dueDate === todayKey ? "today" : "week") as DueBucket,
      remaining: Math.max(0, due.amount - due.totalPaid),
    }))
    .filter((line) => line.remaining > 0)
    .sort((a, b) => a.due.dueDate.localeCompare(b.due.dueDate) || a.due.studentName.localeCompare(b.due.studentName, "tr"));
}

export const DUE_BUCKET_LABEL: Record<DueBucket, string> = {
  overdue: "Vadesi geçmiş",
  today: "Bugün",
  week: `${DUE_WINDOW_DAYS} gün içinde`,
};

// Yönlendirme kartının kısa tarifleri. Anahtarlar sunucudaki NavigationTargets ile aynı;
// tarif yoksa kart yalnızca ekrana götürür.
export const NAVIGATION_TIPS: Record<string, string[]> = {
  attendance: [
    "Dersin satırında Geldi, Gelmedi ya da Mazeretli'yi seç.",
    "Mazeretli seçilince öğrenciye telafi hakkı açılır.",
  ],
  "billing-policy": [
    "Aidatlar ekranının üstünden Fiyat politikası sekmesine geç.",
    "Ücreti yalnızca ders türü (birebir ya da grup) belirler.",
    "Kardeş indirimi öğrencinin künyesinden işaretlenir.",
  ],
  billing: [
    "Birebir öğrencinin kartında 4 ders bir dönemdir; ödemesi karttan alınır.",
    "Grup dersleri kartların altındaki aylık listededir.",
    "Ödeme almak için satırdaki Ödeme al düğmesini kullan.",
  ],
  "students-new": [
    "Kayıt dört adımdır: öğrenci, eğitim (öğretmen ve enstrüman), ders günü, veli.",
    "Ders gününü sonra belirlemek istersen Sonra belirle'yi seç.",
  ],
  calendar: [
    "Dersi sürükleyerek başka saate taşıyabilirsin.",
    "Taşırken yalnız bu hafta mı yoksa her hafta mı olduğunu sorar.",
  ],
  progress: [
    "Öğrenciyi seç, Not ekle ile dersin notunu yaz.",
    "Çalışılan eser ve ödev aynı formda girilir.",
  ],
  "change-requests": [
    "Velilerin ve öğretmenlerin ders değişikliği talepleri burada bekler.",
    "Onaylayınca ders yeni saatine taşınır.",
  ],
  availability: [
    "Hangi öğretmenin hangi gün ve saatte boş olduğunu gösterir.",
    "Yeni öğrenci için enstrüman ve ders süresini seç.",
  ],
  settings: ["Şifreni ve yazı boyutunu Ayarlar'dan değiştirebilirsin."],
};

// Sunucunun verdiği yol ekranı açar; bazı anahtarlar ekranın içindeki sekmeyi de seçer.
export function navigationHref(key: string, path: string) {
  return key === "billing-policy" ? `${path}?view=pricing` : path;
}
