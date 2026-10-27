"use client";

import Link from "next/link";
import type { ReactNode } from "react";
import { Icon } from "@/components/icons";
import { initials, type AssistantStudent } from "@/lib/assistant";
import { COURSE_KIND_LABEL } from "@/lib/billing";
import { useAssistant } from "./assistant-provider";

// Asistan kartlarının ortak parçaları. Kart çerçevesi tek yerde: başlıkta öğrenci, altta
// ilgili ekrana götüren bağlantılar.
export function AssistantCard({ children }: { children: ReactNode }) {
  return <div className="space-y-3 rounded-2xl border border-[var(--line)] bg-[var(--surface)] p-3.5 shadow-[0_1px_2px_rgba(84,43,28,.05)]">{children}</div>;
}

export function StudentHeading({ student, meta }: { student: AssistantStudent; meta?: string }) {
  const course = student.courses.map((item) => `${item.instrumentName} · ${COURSE_KIND_LABEL[item.courseKind]} · ${item.teacherName}`).join(" / ");
  return (
    <div className="flex items-center gap-3">
      <span className="grid h-10 w-10 shrink-0 place-items-center rounded-full bg-[var(--brand-soft)] text-xs font-extrabold text-[var(--brand-strong)]">{initials(student.fullName)}</span>
      <span className="min-w-0">
        <span className="block truncate text-sm font-bold">{student.fullName}{student.status === "Inactive" ? " · pasif" : ""}</span>
        <span className="text-meta line-clamp-2 text-xs">{[course || "Aktif kursu yok", meta].filter(Boolean).join(" · ")}</span>
      </span>
    </div>
  );
}

// İlgili ekrana gider ve paneli kapatır.
export function GoLink({ href, children }: { href: string; children: ReactNode }) {
  const { close } = useAssistant();
  return (
    <Link href={href} onClick={close} className="pressable inline-flex min-h-10 items-center gap-1.5 rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)] hover:border-[var(--brand)]">
      {children}<Icon name="arrow-right" className="h-3.5 w-3.5" />
    </Link>
  );
}

// Sohbete yeni bir istek yazar ("Saatini değiştir" gibi kısayollar).
export function AskButton({ text, studentId, children }: { text: string; studentId?: string; children: ReactNode }) {
  const { ask } = useAssistant();
  return (
    <button type="button" onClick={() => ask(text, studentId)} className="pressable inline-flex min-h-10 items-center rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)] hover:border-[var(--brand)]">
      {children}
    </button>
  );
}

export function Actions({ children }: { children: ReactNode }) {
  return <div className="flex flex-wrap gap-2">{children}</div>;
}

export function Choice({ onClick, disabled, selected, children }: { onClick: () => void; disabled?: boolean; selected?: boolean; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      aria-pressed={selected}
      className={`pressable min-h-10 rounded-full border px-3.5 text-xs font-bold disabled:cursor-not-allowed disabled:opacity-45 ${selected ? "border-[var(--brand)] bg-[var(--brand-soft)] text-[var(--brand-strong)]" : "border-[var(--line)] bg-white text-[var(--foreground)] hover:border-[var(--brand)]"}`}
    >
      {children}
    </button>
  );
}

export function Loading({ children = "Yükleniyor…" }: { children?: ReactNode }) {
  return <p className="text-meta text-sm" role="status">{children}</p>;
}

export function ErrorLine({ children }: { children: ReactNode }) {
  return <p className="rounded-xl bg-[var(--danger-soft)] px-3 py-2 text-sm font-semibold text-[var(--danger-strong)]" role="alert">{children}</p>;
}

export function WarningLine({ children }: { children: ReactNode }) {
  return <p className="rounded-xl bg-[var(--warning-soft)] px-3 py-2 text-sm text-[var(--foreground)]">{children}</p>;
}

export function DoneLine({ children }: { children: ReactNode }) {
  return <p className="flex items-start gap-2 text-sm font-bold text-[var(--success-strong)]" role="status"><Icon name="check" className="mt-0.5 h-4 w-4 shrink-0" />{children}</p>;
}

// Yazma işleminden önceki onay kutusu: ne değişeceği satır satır, kartın neyi denetlediği
// (müsaitlik, çakışma, tatil) ve altında Vazgeç / Onayla. Ders ve telafi işlerinde kullanıcı
// onaylamadan önce neyin doğrulandığını görür.
export function ConfirmBox({ title, rows, checks = [], warning, note, confirmLabel, pending, onConfirm, onCancel }: {
  title: string;
  rows: { label: string; value: ReactNode }[];
  checks?: string[];
  warning?: ReactNode;
  note?: ReactNode;
  confirmLabel: string;
  pending: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}) {
  return (
    <div className="space-y-3 rounded-2xl border-[1.5px] border-[var(--brand)] bg-[var(--brand-soft)]/50 p-3.5">
      <p className="text-sm font-extrabold">{title}</p>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-3 gap-y-1.5 text-sm">
        {rows.map((row) => <div key={row.label} className="contents"><dt className="text-[var(--muted)]">{row.label}</dt><dd className="font-semibold">{row.value}</dd></div>)}
      </dl>
      {checks.length > 0 && (
        <ul className="space-y-1 rounded-xl bg-white/70 px-3 py-2 text-xs" aria-label="Denetlenenler">
          {checks.map((check) => <li key={check} className="flex items-start gap-1.5 text-[var(--success-strong)]"><Icon name="check" className="mt-px h-3.5 w-3.5 shrink-0" /><span className="text-[var(--foreground)]">{check}</span></li>)}
        </ul>
      )}
      {warning && <WarningLine>{warning}</WarningLine>}
      {note && <p className="text-meta text-xs">{note}</p>}
      <div className="flex gap-2">
        <button type="button" onClick={onCancel} disabled={pending} className="btn btn-quiet flex-1 text-xs sm:flex-none">Vazgeç</button>
        <button type="button" onClick={onConfirm} disabled={pending} className="btn btn-primary flex-1 text-xs sm:ml-auto sm:flex-none">{pending ? "Kaydediliyor…" : confirmLabel}</button>
      </div>
    </div>
  );
}
