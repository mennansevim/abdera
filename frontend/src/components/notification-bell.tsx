"use client";

import { useState } from "react";
import { useRouter } from "next/navigation";
import { Icon } from "./icons";
import { Modal } from "./ui";
import {
  useMarkAllStaffNotificationsRead,
  useMarkStaffNotificationRead,
  useStaffNotifications,
  type StaffNotification,
} from "@/lib/messaging";

// Öğretmenin ekranındaki bildirim zili. Şimdilik tek bir olay besliyor: yönetici takvimde
// dersi taşıdığında (docs/11-progress-log.md) öğretmenin programı sessizce değişmesin.
// Panel, konumlandırma sorunu çıkarmaması için ortak Modal ile açılır - mobilde ve
// masaüstünde aynı davranır.
export function NotificationBell({ variant = "sidebar" }: { variant?: "sidebar" | "mobile" }) {
  const [open, setOpen] = useState(false);
  const { data } = useStaffNotifications();
  const markRead = useMarkStaffNotificationRead();
  const markAllRead = useMarkAllStaffNotificationsRead();
  const router = useRouter();
  const unread = data?.unreadCount ?? 0;
  const items = data?.items ?? [];

  function openNotification(notification: StaffNotification) {
    if (!notification.readAt) markRead.mutate(notification.id);
    setOpen(false);
    // Bildirimlerin tamamı ders programıyla ilgili; kullanıcıyı değişikliği görebileceği
    // ekrana bırakıyoruz. Hafta seçimi takvimin kendi durumunda, metindeki tarih yönlendirir.
    router.push("/dashboard/calendar");
  }

  return (
    <>
      {variant === "sidebar" ? (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="pressable relative grid h-10 w-10 place-items-center rounded-lg text-white/75 hover:bg-white/15 hover:text-white"
          aria-label={unread ? `Bildirimler · ${unread} okunmamış` : "Bildirimler"}
          title="Bildirimler"
        >
          <Icon name="bell" className="h-4 w-4" />
          {unread > 0 && <span className="absolute right-1.5 top-1.5 h-2 w-2 rounded-full bg-[#ffe27a] ring-1 ring-black/20" />}
        </button>
      ) : (
        <button
          type="button"
          onClick={() => setOpen(true)}
          className="pressable relative flex min-h-14 flex-col items-center justify-center gap-1 rounded-xl text-[.61rem] font-medium text-[var(--muted)]"
          aria-label={unread ? `Bildirimler · ${unread} okunmamış` : "Bildirimler"}
        >
          <Icon name="bell" className="h-[1.05rem] w-[1.05rem]" />
          <span>Bildirim</span>
          {unread > 0 && <span className="absolute right-[22%] top-2 h-2 w-2 rounded-full bg-[var(--danger)] ring-2 ring-white" />}
        </button>
      )}

      <Modal open={open} title="Bildirimler" onClose={() => setOpen(false)} size="sm">
        {items.length === 0 ? (
          <p className="text-meta py-6 text-center">Yeni bildirim yok.</p>
        ) : (
          <div className="space-y-3">
            <ul className="space-y-2">
              {items.map((notification) => (
                <li key={notification.id}>
                  <button
                    type="button"
                    onClick={() => openNotification(notification)}
                    className={`pressable flex w-full items-start gap-3 rounded-xl border p-3 text-left ${
                      notification.readAt ? "border-[var(--line)] bg-white" : "border-[var(--brand)]/30 bg-[var(--brand-soft)]/45"
                    }`}
                  >
                    <span className={`mt-0.5 grid h-8 w-8 shrink-0 place-items-center rounded-lg ${notification.readAt ? "bg-[var(--surface-muted)] text-[var(--muted)]" : "bg-[var(--brand-soft)] text-[var(--brand-strong)]"}`}>
                      <Icon name="calendar" className="h-4 w-4" />
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
              ))}
            </ul>
            {(data?.unreadCount ?? 0) > 0 && (
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

function relativeTime(value: string) {
  const minutes = Math.round((Date.now() - new Date(value).getTime()) / 60_000);
  if (minutes < 1) return "az önce";
  if (minutes < 60) return `${minutes} dakika önce`;
  const hours = Math.round(minutes / 60);
  if (hours < 24) return `${hours} saat önce`;
  return new Date(value).toLocaleDateString("tr-TR", { day: "numeric", month: "long", hour: "2-digit", minute: "2-digit" });
}
