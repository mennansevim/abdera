"use client";

import { useRouter } from "next/navigation";
import { useEffect, useMemo, useState } from "react";
import { AppLoader } from "@/components/app-loader";
import { Icon, type IconName } from "@/components/icons";
import { Modal, Notice } from "@/components/ui";
import { useGuardianLogout, useRequireGuardianAuth } from "@/lib/guardian-auth";
import {
  useGuardianBilling,
  useGuardianCalendar,
  useGuardianMessages,
  useGuardianPracticeJournal,
  useGuardianProgress,
  useGuardianStudents,
  type GuardianStudent,
} from "@/lib/guardian";
import { GuardianChangePasswordForm } from "./change-password-form";
import { BillingView } from "./_views/billing";
import { CalendarView } from "./_views/calendar";
import { HomeView } from "./_views/home";
import { MessagesView } from "./_views/messages";
import { ProgressView } from "./_views/progress";
import { addDays, isOpenReceivable } from "./_views/shared";

type ParentTab = "home" | "calendar" | "progress" | "billing" | "messages";

const TABS: { id: ParentTab; label: string; icon: IconName }[] = [
  { id: "home", label: "Ana Sayfa", icon: "home" },
  { id: "calendar", label: "Takvim", icon: "calendar" },
  { id: "progress", label: "Gelişim", icon: "activity" },
  { id: "billing", label: "Aidat", icon: "wallet" },
  { id: "messages", label: "Mesajlar", icon: "bell" },
];

// Veli portalı telefon için tasarlandı: tek sütun, sayfanın kendisi kayar (iç kutu değil - iOS'ta
// adres çubuğu küçülür, "yukarı kaydır" hareketi doğal çalışır), alt menü ekrana sabit. Geniş
// ekranda aynı sütun ortalanır; ayrı bir masaüstü yerleşimi yok.
export default function ParentPage() {
  const router = useRouter();
  const logout = useGuardianLogout();
  const { guardian, isLoading: guardianLoading } = useRequireGuardianAuth();
  const { data: students, isLoading: studentsLoading } = useGuardianStudents();
  const billingQuery = useGuardianBilling();
  const messagesQuery = useGuardianMessages();
  const [tab, setTab] = useState<ParentTab>("home");
  const [studentIndex, setStudentIndex] = useState(0);
  const [today] = useState(() => new Date());
  const [showPasswordForm, setShowPasswordForm] = useState(false);
  const [passwordChanged, setPasswordChanged] = useState(false);

  const selectedStudent = students?.[studentIndex % Math.max(students.length, 1)];
  const studentId = selectedStudent?.studentId;
  const from = useMemo(() => today.toISOString(), [today]);
  const to = useMemo(() => addDays(today, 60).toISOString(), [today]);
  const calendarQuery = useGuardianCalendar(studentId, from, to);
  const progressQuery = useGuardianProgress(studentId);
  const journalQuery = useGuardianPracticeJournal(studentId);

  // Uzun bir sekmenin ortasından kısa bir sekmeye geçen veli boş bir ekran görmesin.
  useEffect(() => {
    window.scrollTo({ top: 0 });
  }, [tab, studentId]);

  function handleLogout() {
    logout.mutate(undefined, { onSettled: () => router.replace("/parent/login") });
  }

  if (guardianLoading || studentsLoading || !guardian) {
    return <AppLoader message="Veli portalı açılıyor…" />;
  }

  const hasOpenBilling = (billingQuery.data?.enrollments ?? [])
    .some((enrollment) => enrollment.studentId === studentId && enrollment.receivables.some(isOpenReceivable));

  return (
    <div className="min-h-dvh bg-[var(--background)]">
      <header className="sticky top-0 z-30 border-b border-[var(--line)] bg-[color-mix(in_srgb,var(--background)_88%,transparent)] pt-[env(safe-area-inset-top)] backdrop-blur-xl">
        <div className="mx-auto flex max-w-2xl items-center gap-3 px-4 py-2.5">
          <div className="min-w-0 flex-1">
            <p className="truncate text-[.8125rem] text-[var(--muted)]">Merhaba, {guardian.firstName}</p>
            {students && students.length > 1
              ? <p className="truncate text-base font-bold">Veli portalı</p>
              : selectedStudent
                ? <StudentLine student={selectedStudent} />
                : <p className="text-sm text-[var(--muted)]">Bağlı öğrenci bulunamadı</p>}
          </div>
          <HeaderMenu
            onOpenMain={() => router.push("/login?chooseRole=1")}
            onChangePassword={() => setShowPasswordForm(true)}
            onLogout={handleLogout}
            loggingOut={logout.isPending}
          />
        </div>
        {students && students.length > 1 && (
          <StudentSwitcher students={students} selectedIndex={studentIndex % students.length} onSelect={setStudentIndex} />
        )}
      </header>

      <main className="mx-auto max-w-2xl px-4 pb-[calc(6rem+env(safe-area-inset-bottom))] pt-4">
        {passwordChanged && <div className="mb-3"><Notice onDismiss={() => setPasswordChanged(false)}>Şifren değiştirildi.</Notice></div>}
        {/* Yalnızca ana sayfada: her sekmede tekrar etmesi asıl içeriği aşağı itiyordu. */}
        {tab === "home" && guardian.usesDefaultPassword && !passwordChanged && (
          <div role="status" className="mb-4 flex items-center gap-3 rounded-2xl bg-[var(--warning-soft)] px-4 py-3 text-[var(--warning-strong)]">
            <Icon name="shield" className="h-5 w-5 shrink-0" />
            <p className="min-w-0 flex-1 text-sm font-semibold">Güvenliğin için okulun verdiği ilk şifreyi değiştir.</p>
            <button type="button" onClick={() => setShowPasswordForm(true)} className="pressable min-h-11 shrink-0 rounded-xl bg-white px-3 text-sm font-bold">Değiştir</button>
          </div>
        )}

        {tab === "home" && (
          <HomeView
            lessons={calendarQuery.data}
            lessonsLoading={calendarQuery.isLoading}
            lessonsError={calendarQuery.isError}
            refetchLessons={calendarQuery.refetch}
            messages={messagesQuery.data}
            messagesError={messagesQuery.isError}
            billing={billingQuery.data}
            studentId={studentId}
            today={today}
            onNavigate={setTab}
          />
        )}
        {tab === "calendar" && <CalendarView lessons={calendarQuery.data} loading={calendarQuery.isLoading} error={calendarQuery.isError} refetch={calendarQuery.refetch} today={today} />}
        {tab === "progress" && <ProgressView studentId={studentId} progress={progressQuery.data} practiceJournal={journalQuery.data} loading={progressQuery.isLoading || journalQuery.isLoading} />}
        {tab === "billing" && <BillingView billing={billingQuery.data} loading={billingQuery.isLoading} error={billingQuery.isError} refetch={billingQuery.refetch} fetching={billingQuery.isFetching} studentId={studentId} />}
        {tab === "messages" && <MessagesView messages={messagesQuery.data} loading={messagesQuery.isLoading} error={messagesQuery.isError} refetch={messagesQuery.refetch} />}
      </main>

      <ParentNavigation tab={tab} setTab={setTab} billingAlert={hasOpenBilling} />

      <Modal open={showPasswordForm} title="Şifremi değiştir" description="Giriş yaparken telefon numaranla birlikte bu şifreyi kullanacaksın." onClose={() => setShowPasswordForm(false)} size="sm">
        <GuardianChangePasswordForm onClose={() => setShowPasswordForm(false)} onDone={() => { setShowPasswordForm(false); setPasswordChanged(true); }} />
      </Modal>
    </div>
  );
}

function StudentLine({ student }: { student: GuardianStudent }) {
  return (
    <p className="flex min-w-0 items-baseline gap-1.5">
      <span className="truncate text-base font-bold">{student.firstName} {student.lastName}</span>
      {student.instrumentName && <span className="shrink-0 text-[.8125rem] text-[var(--muted)]">{student.instrumentName}</span>}
    </p>
  );
}

// Birden fazla çocuğu olan veli için öğrenci seçimi önceden "…" menüsünün içindeydi ve fark
// edilmiyordu; artık başlığın altında her zaman görünen bir sekme şeridi.
function StudentSwitcher({ students, selectedIndex, onSelect }: { students: GuardianStudent[]; selectedIndex: number; onSelect: (index: number) => void }) {
  return (
    <div role="radiogroup" aria-label="Öğrenci seç" className="mx-auto flex max-w-2xl gap-2 overflow-x-auto px-4 pb-2.5 [scrollbar-width:none]">
      {students.map((student, index) => {
        const selected = index === selectedIndex;
        return (
          <button
            key={student.studentId}
            type="button"
            role="radio"
            aria-checked={selected}
            onClick={() => onSelect(index)}
            className={`pressable flex min-h-11 shrink-0 items-center gap-2 rounded-full py-1 pl-1 pr-4 text-sm font-bold ${selected ? "bg-[var(--brand-strong)] text-white" : "border border-[var(--line)] bg-white text-[var(--foreground)]"}`}
          >
            <span className={`grid h-8 w-8 place-items-center rounded-full text-[.75rem] ${selected ? "bg-white/20" : "bg-[var(--brand-soft)] text-[var(--brand-strong)]"}`}>
              {`${student.firstName.charAt(0)}${student.lastName.charAt(0)}`.toLocaleUpperCase("tr-TR")}
            </span>
            <span>{student.firstName} {student.lastName}</span>
          </button>
        );
      })}
    </div>
  );
}

function HeaderMenu({ onOpenMain, onChangePassword, onLogout, loggingOut }: {
  onOpenMain: () => void;
  onChangePassword: () => void;
  onLogout: () => void;
  loggingOut: boolean;
}) {
  const [open, setOpen] = useState(false);

  useEffect(() => {
    if (!open) return;
    const onKeyDown = (event: KeyboardEvent) => { if (event.key === "Escape") setOpen(false); };
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [open]);

  const itemClass = "pressable flex min-h-12 w-full items-center gap-3 rounded-xl px-3 text-left text-sm font-semibold hover:bg-black/[.035] disabled:opacity-50";

  return (
    <div className="relative shrink-0">
      <button type="button" onClick={() => setOpen((value) => !value)} className="pressable grid h-11 w-11 place-items-center rounded-full border border-[var(--line)] bg-white text-[var(--muted)]" aria-label="Menüyü aç" aria-expanded={open}>
        <Icon name="settings" className="h-5 w-5" />
      </button>
      {open && (
        <>
          <button type="button" aria-label="Menüyü kapat" onClick={() => setOpen(false)} className="fixed inset-0 z-40 cursor-default" />
          <div role="menu" className="app-card absolute right-0 top-[calc(100%+.5rem)] z-50 w-60 overflow-hidden p-1.5">
            <button type="button" role="menuitem" onClick={() => { onChangePassword(); setOpen(false); }} className={itemClass}>
              <Icon name="shield" className="h-5 w-5 text-[var(--muted)]" /> Şifremi değiştir
            </button>
            <button type="button" role="menuitem" onClick={() => { onOpenMain(); setOpen(false); }} className={itemClass}>
              <Icon name="swap" className="h-5 w-5 text-[var(--muted)]" /> Başka hesapla gir
            </button>
            <div className="my-1 border-t border-[var(--line)]" />
            <button type="button" role="menuitem" onClick={onLogout} disabled={loggingOut} className={`${itemClass} text-[var(--danger-strong)]`}>
              <Icon name="logout" className="h-5 w-5" /> {loggingOut ? "Çıkış yapılıyor…" : "Çıkış yap"}
            </button>
          </div>
        </>
      )}
    </div>
  );
}

function ParentNavigation({ tab, setTab, billingAlert }: { tab: ParentTab; setTab: (tab: ParentTab) => void; billingAlert: boolean }) {
  return (
    <nav aria-label="Veli menüsü" className="fixed inset-x-0 bottom-0 z-30 border-t border-[var(--line)] bg-white/95 pb-[env(safe-area-inset-bottom)] backdrop-blur-xl">
      <div className="mx-auto grid max-w-2xl grid-cols-5 px-1">
        {TABS.map((item) => {
          const active = tab === item.id;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => setTab(item.id)}
              aria-current={active ? "page" : undefined}
              className={`pressable flex min-h-16 flex-col items-center justify-center gap-1 text-[.75rem] font-bold ${active ? "text-[var(--brand-strong)]" : "text-[var(--muted)]"}`}
            >
              <span className={`relative grid h-8 w-14 place-items-center rounded-full transition-colors ${active ? "bg-[var(--brand-soft)]" : ""}`}>
                <Icon name={item.icon} className="h-5 w-5" />
                {item.id === "billing" && billingAlert && <span aria-hidden="true" className="absolute right-3 top-1 h-2 w-2 rounded-full bg-[var(--danger)] ring-2 ring-white" />}
              </span>
              {item.label}
            </button>
          );
        })}
      </div>
    </nav>
  );
}
