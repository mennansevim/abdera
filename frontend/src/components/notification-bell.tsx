"use client";

import { useEffect, useRef, useState } from "react";
import { usePathname, useRouter } from "next/navigation";
import { Icon, type IconName } from "./icons";
import { Modal } from "./ui";
import { ApiError } from "@/lib/api";
import { useMarkAttendance } from "@/lib/attendance";
import {
  useMarkAllStaffNotificationsRead,
  useMarkStaffNotificationRead,
  useStaffNotifications,
  type StaffNotification,
  type StaffNotificationType,
} from "@/lib/messaging";

// Personelin (yönetici + öğretmen) bildirimleri üç yoldan görünür (kullanıcı isteği: "ekranda
// popup gibi, bildirimler kısmında badge ile ve mail ile"):
//   1. Zil + sayılı rozet: okunmamış bildirim sayısı (NotificationBell).
//   2. Açılır kart: yeni bir bildirim düştüğünde ekranın köşesinde açılır ve üzerine
//      tıklanana kadar kalır (kullanıcı isteği: "popup olarak gelsin, üzerine tıklayınca
//      kaybolsun"); tıklamak bildirimi okundu da sayar (NotificationToasts). Sayfa açıkken
//      dakikada iki kez yoklanır. Her bildirim bu tarayıcıda YALNIZCA BİR KEZ kart olur
//      (kullanıcı isteği: "1 kere göründükten sonra göstermeyelim, bildirim içine badge
//      olarak kalsın"): yeniden öne çıkan bir hatırlatma (LessonNoteMissing üç günde bir,
//      RefreshReminder) zilde okunmamış sayılır ama kart olarak tekrar açılmaz.
//   3. E-posta: sunucu tarafında, bildirim oluştuğu anda (IStaffNotifier.FlushEmailsAsync).
// Panel, konumlandırma sorunu çıkarmaması için ortak Modal ile açılır - mobilde ve
// masaüstünde aynı davranır.

const TYPE_META: Record<StaffNotificationType, { icon: IconName; tone: string; href: string }> = {
  LessonMoved: { icon: "swap", tone: "bg-[var(--warning-soft)] text-[var(--warning-strong)]", href: "/dashboard/calendar" },
  LessonCancelled: { icon: "x", tone: "bg-[var(--danger-soft)] text-[var(--danger-strong)]", href: "/dashboard/calendar" },
  MakeupScheduled: { icon: "calendar", tone: "bg-[var(--success-soft)] text-[var(--success-strong)]", href: "/dashboard/calendar" },
  StudentDeletionRequested: { icon: "students", tone: "bg-[var(--brand-soft)] text-[var(--brand-strong)]", href: "/dashboard/change-requests" },
  // Tek, tazelenen hatırlatma: notu yazılmamış dersler öğretmenin Gelişim ekranındaki kartta listelenir.
  LessonNoteMissing: { icon: "note", tone: "bg-[var(--warning-soft)] text-[var(--warning-strong)]", href: "/dashboard/progress#yorum-bekleyen-dersler" },
  // "Öğrenci geldi mi?" - satırın kendisi değil Geldi/Gelmedi düğmeleri cevaplar (AttendanceQuestion).
  AttendanceMissing: { icon: "students", tone: "bg-[var(--brand-soft)] text-[var(--brand-strong)]", href: "/dashboard/calendar" },
  // Veli "gelemiyor" / "gecikecek" dedi; takvimde aynı ders ünlemle işaretlidir.
  GuardianRsvp: { icon: "alert-triangle", tone: "bg-[var(--danger-soft)] text-[var(--danger-strong)]", href: "/dashboard/calendar" },
};

function metaFor(type: StaffNotificationType) {
  return TYPE_META[type] ?? TYPE_META.LessonMoved;
}

// Cevap bekleyen yoklama sorusu. Okundu işaretlemekle kapanmaz (sunucu reddeder); yalnızca
// yoklama girilince kapanır. Cevaplanmış soru listede tekrar gösterilmez - iş bitti.
function isOpenQuestion(notification: StaffNotification) {
  return notification.type === "AttendanceMissing" && !notification.readAt;
}

// Kullanıcı isteği: "bildirim ekranında buton olsun geldi gelmedi diye. seçim yapılmayanlar
// bildirimlerde kalsın." Cevap doğrudan yoklama ucuna gider (dersin id'si referenceId);
// sunucu yoklamayla birlikte soruyu kapatır, liste yenilenince satır düşer.
function AttendanceQuestion({ notification, onAnswered }: { notification: StaffNotification; onAnswered?: () => void }) {
  const markAttendance = useMarkAttendance(notification.referenceId);
  const [error, setError] = useState<string | null>(null);

  function answer(status: "Present" | "Absent") {
    setError(null);
    markAttendance.mutate({ status }, {
      onSuccess: () => onAnswered?.(),
      onError: (err) => setError(err instanceof ApiError ? (err.detail ?? err.title) : "Yoklama kaydedilemedi."),
    });
  }

  const pending = markAttendance.isPending;
  return (
    <div className="rounded-xl border border-[var(--brand)]/30 bg-[var(--brand-soft)]/45 p-3">
      <div className="flex items-start gap-3">
        <span className="mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-lg bg-[var(--brand-soft)] text-[var(--brand-strong)]">
          <Icon name="students" className="h-4 w-4" />
        </span>
        <span className="min-w-0 flex-1 pr-6">
          <span className="block text-xs font-bold">{notification.title}</span>
          <span className="text-meta mt-1 block">{notification.body}</span>
        </span>
      </div>
      <div className="mt-2.5 grid grid-cols-2 gap-2 pl-11">
        <button type="button" onClick={() => answer("Present")} disabled={pending} className="pressable min-h-10 rounded-lg bg-[var(--success-strong)] px-3 text-xs font-bold text-white disabled:opacity-60">
          {pending && markAttendance.variables?.status === "Present" ? "Kaydediliyor…" : "Geldi"}
        </button>
        <button type="button" onClick={() => answer("Absent")} disabled={pending} className="pressable min-h-10 rounded-lg border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--danger-strong)] disabled:opacity-60">
          {pending && markAttendance.variables?.status === "Absent" ? "Kaydediliyor…" : "Gelmedi"}
        </button>
      </div>
      {error && <p role="alert" className="mt-2 pl-11 text-[.75rem] font-semibold text-[var(--danger-strong)]">{error}</p>}
    </div>
  );
}

function badgeText(count: number) {
  return count > 9 ? "9+" : String(count);
}

function useOpenNotification() {
  const markRead = useMarkStaffNotificationRead();
  const router = useRouter();
  return (notification: StaffNotification) => {
    if (!notification.readAt) markRead.mutate(notification.id);
    router.push(metaFor(notification.type).href);
  };
}

export function NotificationBell({ variant = "sidebar" }: { variant?: "sidebar" | "mobile" | "header" }) {
  const [open, setOpen] = useState(false);
  const { data } = useStaffNotifications();
  const markAllRead = useMarkAllStaffNotificationsRead();
  const openNotification = useOpenNotification();
  const unread = data?.unreadCount ?? 0;
  const allItems = data?.items ?? [];
  // Cevap bekleyen yoklama soruları listenin en üstünde ayrı durur; cevaplanmış olanlar
  // gösterilmez. Rozet onları da sayar - soru cevaplanana kadar zil boşalmaz.
  const questions = allItems.filter(isOpenQuestion).sort((a, b) => a.createdAt.localeCompare(b.createdAt));
  const items = allItems.filter((item) => item.type !== "AttendanceMissing");
  const unreadOthers = items.filter((item) => !item.readAt).length;
  const label = unread ? `Bildirimler · ${unread} okunmamış` : "Bildirimler";

  const badge = unread > 0 && (
    <span
      aria-hidden="true"
      className={`absolute grid min-w-[1.15rem] place-items-center rounded-full px-1 text-[.65rem] font-extrabold leading-[1.15rem] tabular-nums ${
        variant === "sidebar"
          ? "right-0.5 top-0.5 bg-[#ffe27a] text-[#5a3a00] ring-1 ring-black/20"
          : "bg-[var(--danger)] text-white ring-2 ring-white " + (variant === "mobile" ? "right-[18%] top-1" : "-right-1 -top-1")
      }`}
    >
      {badgeText(unread)}
    </span>
  );

  return (
    <>
      {variant === "sidebar" && (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="pressable relative grid h-11 w-11 place-items-center rounded-lg text-white/75 hover:bg-white/15 hover:text-white"
          aria-label={label}
          title="Bildirimler"
        >
          <Icon name="bell" className="h-4 w-4" />
          {badge}
        </button>
      )}
      {variant === "mobile" && (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="pressable relative flex min-h-14 flex-col items-center justify-center gap-1 rounded-xl text-[.75rem] font-medium text-[var(--muted)]"
          aria-label={label}
        >
          <Icon name="bell" className="h-[1.05rem] w-[1.05rem]" />
          <span>Bildirim</span>
          {badge}
        </button>
      )}
      {variant === "header" && (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="pressable relative grid h-11 w-11 place-items-center rounded-xl border border-[var(--line)] bg-white text-[var(--brand-strong)]"
          aria-label={label}
        >
          <Icon name="bell" className="h-5 w-5" />
          {badge}
        </button>
      )}

      <Modal open={open} title="Bildirimler" onClose={() => setOpen(false)} size="sm">
        {items.length === 0 && questions.length === 0 ? (
          <p className="text-meta py-6 text-center">Yeni bildirim yok.</p>
        ) : (
          <div className="space-y-3">
            {questions.length > 0 && (
              <section aria-label="Yoklama bekleyen dersler" className="space-y-2">
                <h3 className="text-micro text-[var(--brand-strong)]">Yoklama bekleyen dersler · {questions.length}</h3>
                <ul className="space-y-2">
                  {questions.map((question) => <li key={question.id}><AttendanceQuestion notification={question} /></li>)}
                </ul>
              </section>
            )}
            {questions.length > 0 && items.length > 0 && <div className="border-t border-[var(--line)]" aria-hidden="true" />}
            <ul className="space-y-2">
              {items.map((notification) => {
                const meta = metaFor(notification.type);
                return (
                  <li key={notification.id}>
                    <button
                      type="button"
                      onClick={() => { setOpen(false); openNotification(notification); }}
                      className={`pressable flex w-full items-start gap-3 rounded-xl border p-3 text-left ${
                        notification.readAt ? "border-[var(--line)] bg-white" : "border-[var(--brand)]/30 bg-[var(--brand-soft)]/45"
                      }`}
                    >
                      <span className={`mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-lg ${notification.readAt ? "bg-[var(--surface-muted)] text-[var(--muted)]" : meta.tone}`}>
                        <Icon name={meta.icon} className="h-4 w-4" />
                      </span>
                      <span className="min-w-0 flex-1">
                        <span className="flex items-center gap-2">
                          <span className="text-xs font-bold">{notification.title}</span>
                          {!notification.readAt && <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-[var(--brand)]" aria-label="Okunmadı" />}
                        </span>
                        <span className="text-meta mt-1 block">{notification.body}</span>
                        <span className="text-meta mt-1 block">{relativeTime(notification.createdAt)}</span>
                      </span>
                    </button>
                  </li>
                );
              })}
            </ul>
            {unreadOthers > 0 && (
              <div className="flex justify-end border-t border-[var(--line)] pt-3">
                <button type="button" onClick={() => markAllRead.mutate()} disabled={markAllRead.isPending} className="btn btn-quiet">
                  Tümünü okundu işaretle
                </button>
              </div>
            )}
          </div>
        )}
      </Modal>
    </>
  );
}

// Tarayıcı başına "şu ana kadar gösterilen en yeni bildirim" damgası. Yalnızca kolaylık:
// silinirse (gizli sekme, temizlenen site verisi) ilk açılışta eski bildirimler kart olarak
// fışkırmaz, sadece zilde sayılır. Bildirimin kendisi sunucuda; burada hiçbir şey kaybolmaz.
const SEEN_KEY = "abdera:notifications:toasted-until";
// Kart olarak gösterilmiş bildirim kimlikleri. Hatırlatmalar yeniden öne çıkarken createdAt'leri
// tazelendiği için yalnızca zaman damgasına bakmak aynı kartı tekrar açıyordu.
const TOASTED_IDS_KEY = "abdera:notifications:toasted-ids";
const MAX_TOASTED_IDS = 300;
const MAX_TOASTS = 3;

function readToastedIds(): string[] {
  try {
    const parsed: unknown = JSON.parse(window.localStorage.getItem(TOASTED_IDS_KEY) ?? "[]");
    return Array.isArray(parsed) ? parsed.filter((item): item is string => typeof item === "string") : [];
  } catch {
    return [];
  }
}

function writeToastedIds(ids: Set<string>) {
  try {
    window.localStorage.setItem(TOASTED_IDS_KEY, JSON.stringify([...ids].slice(-MAX_TOASTED_IDS)));
  } catch {
    // Depolama kapalıysa kimlikler bu oturum boyunca bellekte tutulur (shownIds).
  }
}

function readSeen(): string | null {
  try {
    return window.localStorage.getItem(SEEN_KEY);
  } catch {
    return null;
  }
}

function writeSeen(value: string) {
  try {
    window.localStorage.setItem(SEEN_KEY, value);
  } catch {
    // Depolama kapalıysa kartlar yine bu oturum boyunca bir kez gösterilir (shownIds).
  }
}

// Yeni bildirim düştüğünde ekranın köşesinde açılan kart + sekme başlığında okunmamış sayısı.
// Uygulama kabuğuna (app-header) bir kez yerleştirilir.
export function NotificationToasts() {
  const pathname = usePathname();
  const { data } = useStaffNotifications();
  const markRead = useMarkStaffNotificationRead();
  const [toasts, setToasts] = useState<StaffNotification[]>([]);
  const shownIds = useRef<Set<string> | null>(null);
  const baseline = useRef<number | undefined>(undefined);
  const unread = data?.unreadCount ?? 0;

  useEffect(() => {
    if (!data) return;
    shownIds.current ??= new Set(readToastedIds());
    const shown = shownIds.current;
    // Karşılaştırma zaman damgasının sayısal değeriyle: sunucu "+00:00", tarayıcı "Z" yazıyor,
    // metin olarak karşılaştırmak yanlış sonuç verebilirdi.
    const newest = data.items.reduce((max, item) => Math.max(max, Date.parse(item.createdAt) || 0), 0);

    // İlk yükleme: önceden görülmüş bir damga yoksa mevcut bildirimleri kart olarak
    // göstermeyiz (hepsi birden açılırdı) - bunlar zaten zilde sayılıyor.
    if (baseline.current === undefined) {
      const stored = Number(readSeen());
      if (!stored) {
        baseline.current = newest || Date.now();
        writeSeen(String(baseline.current));
        return;
      }
      baseline.current = stored;
    }

    const since = baseline.current;
    const fresh = data.items.filter((item) => !item.readAt && Date.parse(item.createdAt) > since && !shown.has(item.id));
    if (fresh.length) {
      fresh.forEach((item) => shown.add(item.id));
      writeToastedIds(shown);
      setToasts((current) => [...fresh, ...current].slice(0, MAX_TOASTS));
    }
    if (newest > since) {
      baseline.current = newest;
      writeSeen(String(newest));
    }
  }, [data]);

  // Sekme arka plandayken de fark edilsin: "(2) Abdera". Next.js sayfa geçişinde başlığı
  // yeniden yazdığı için önek her değişimde temizlenip tekrar eklenir.
  useEffect(() => {
    const apply = () => {
      const base = document.title.replace(/^\(\d+\+?\)\s*/, "");
      const next = unread > 0 ? `(${badgeText(unread)}) ${base}` : base;
      if (document.title !== next) document.title = next;
    };
    apply();
    const observer = new MutationObserver(apply);
    const titleElement = document.querySelector("title");
    if (titleElement) observer.observe(titleElement, { childList: true, characterData: true, subtree: true });
    return () => observer.disconnect();
  }, [unread]);

  // Kart kendiliğinden kaybolmaz; tıklanınca kapanır ve bildirim okundu sayılır. Ayrıntıya
  // gitmek isteyen zile basıp listeden açar.
  function dismiss(toast: StaffNotification) {
    setToasts((current) => current.filter((item) => item.id !== toast.id));
    if (!toast.readAt) markRead.mutate(toast.id);
  }

  // "Bugün" ekranında (yönetici ve öğretmen ana ekranı) kart açılmaz (kullanıcı isteği: "bugün
  // ekranında bildirimler gelmesin"). Yeni bildirimler kuyrukta bekler ve başka bir ekrana
  // geçilince açılır; zildeki rozet ve sekme başlığındaki sayı burada da güncellenir.
  if (!toasts.length || pathname === "/dashboard") return null;

  return (
    <div
      className="pointer-events-none fixed inset-x-3 top-3 z-[60] flex flex-col items-stretch gap-2 sm:inset-x-auto sm:right-5 sm:top-5 sm:w-[22rem]"
      role="status"
      aria-live="polite"
    >
      {toasts.map((toast) => {
        const meta = metaFor(toast.type);
        // Ders bitince açılan "öğrenci geldi mi?" kartı düğmeleriyle gelir. Kartı kapatmak
        // soruyu zilden SİLMEZ (sunucu okundu saymaz) - cevaplanana kadar zilde bekler.
        if (toast.type === "AttendanceMissing") {
          return (
            <div key={toast.id} className="toast-in pointer-events-auto relative rounded-2xl bg-white shadow-[0_18px_40px_rgba(60,35,15,.22)]">
              <AttendanceQuestion notification={toast} onAnswered={() => setToasts((current) => current.filter((item) => item.id !== toast.id))} />
              <button
                type="button"
                onClick={() => setToasts((current) => current.filter((item) => item.id !== toast.id))}
                aria-label="Kartı kapat, soru bildirimlerde kalsın"
                title="Sonra cevapla"
                className="pressable absolute right-2 top-2 grid h-7 w-7 place-items-center rounded-lg text-[var(--muted)] hover:bg-[var(--surface-muted)]"
              >
                <Icon name="close" className="h-3.5 w-3.5" />
              </button>
            </div>
          );
        }
        return (
          <button
            key={toast.id}
            type="button"
            onClick={() => dismiss(toast)}
            aria-label={`${toast.title}: ${toast.body}. Kapatmak için tıkla.`}
            className="toast-in pressable pointer-events-auto flex w-full items-start gap-3 rounded-2xl border border-[var(--brand)]/30 bg-white p-3.5 text-left shadow-[0_18px_40px_rgba(60,35,15,.22)] hover:border-[var(--brand)]"
          >
            <span className={`grid h-10 w-10 shrink-0 place-items-center rounded-xl ${meta.tone}`}>
              <Icon name={meta.icon} className="h-4 w-4" />
            </span>
            <span className="min-w-0 flex-1">
              <span className="block text-sm font-extrabold">{toast.title}</span>
              <span className="text-meta mt-0.5 block">{toast.body}</span>
              <span className="mt-1.5 block text-[.75rem] font-semibold text-[var(--muted)]">Kapatmak için tıkla</span>
            </span>
            <Icon name="close" className="mt-0.5 h-3.5 w-3.5 shrink-0 text-[var(--muted)]" aria-hidden="true" />
          </button>
        );
      })}
    </div>
  );
}

function relativeTime(value: string) {
  const minutes = Math.round((Date.now() - new Date(value).getTime()) / 60_000);
  if (minutes < 1) return "az önce";
  if (minutes < 60) return `${minutes} dakika önce`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} saat önce`;
  return new Date(value).toLocaleDateString("tr-TR", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
}
