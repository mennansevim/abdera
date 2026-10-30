"use client";

import { useState } from "react";
import { DUE_BUCKET_LABEL, openDueLines, type AssistantStudent, type DueBucket, type DueLine } from "@/lib/assistant";
import { COURSE_KIND_LABEL, PAYMENT_REMINDER_OUTCOME_TEXT, useBillingDues, useSendPaymentReminder, type PaymentReminderOutcome } from "@/lib/billing";
import { formatDay, formatMoney, formatPeriod } from "@/lib/billing-format";
import { errorMessage } from "@/lib/library";
import { Actions, AssistantCard, ConfirmBox, DoneLine, ErrorLine, GoLink, Loading, StudentHeading } from "./shared";

// "Aidat günü gelenleri listele": vadesi geçmiş ve 7 gün içinde gelen ödenmemiş aidatlar
// (GET /api/billing/dues). Öğrenci adı verildiyse o öğrencinin bütün açık aidatları.
//
// Asistan para almaz (AE2): "Ödeme al" Aidatlar ekranındaki tek tahsilat penceresini o
// kayıt ve dönemle açar. Hatırlatma mevcut uçtan, onaydan sonra gönderilir.
const BUCKETS: DueBucket[] = ["overdue", "today", "week"];

type Reminder = { ids: string[]; title: string };

export function DuesCard({ student }: { student: AssistantStudent | null }) {
  const dues = useBillingDues();
  const reminder = useSendPaymentReminder();
  const [confirming, setConfirming] = useState<Reminder | null>(null);
  const [sent, setSent] = useState<Record<string, PaymentReminderOutcome>>({});
  const [result, setResult] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  if (dues.isLoading) return <AssistantCard><Loading>Aidatlar getiriliyor…</Loading></AssistantCard>;
  if (dues.isError || !dues.data) return <AssistantCard><ErrorLine>Aidatlar şu an getirilemedi.</ErrorLine></AssistantCard>;

  const lines = openDueLines(dues.data, new Date(), student?.studentId);
  const total = lines.reduce((value, line) => value + line.remaining, 0);
  const currency = lines[0]?.due.currency ?? "TRY";
  const overdue = lines.filter((line) => line.bucket === "overdue" && !sent[line.due.id]);

  async function sendReminders(target: Reminder) {
    setError(null);
    let delivered = 0;
    for (const id of target.ids) {
      try {
        const response = await reminder.mutateAsync(id);
        setSent((current) => ({ ...current, [id]: response.outcome }));
        if (response.scheduled) delivered += 1;
      } catch (err) {
        setError(errorMessage(err, "Hatırlatma gönderilemedi."));
        break;
      }
    }
    setConfirming(null);
    setResult(delivered === target.ids.length
      ? `${delivered} hatırlatma sıraya alındı.`
      : `${delivered}/${target.ids.length} hatırlatma sıraya alındı. Gönderilmeyenlerin nedeni satırlarında yazıyor.`);
  }

  if (lines.length === 0) {
    return (
      <AssistantCard>
        {student && <StudentHeading student={student} />}
        <DoneLine>{student ? `${student.fullName} için ödenmemiş aidat yok.` : "Vadesi geçmiş ya da 7 gün içinde gelen ödenmemiş aidat yok."}</DoneLine>
        <Actions><GoLink href="/dashboard/billing">Aidatlar ekranı</GoLink></Actions>
      </AssistantCard>
    );
  }

  return (
    <AssistantCard>
      {student && <StudentHeading student={student} />}
      <p className="text-sm">
        {student ? "Ödenmemiş" : "Vadesi geçmiş ve 7 gün içinde gelen"} <b className="tabular-nums">{lines.length}</b> aidat, toplam <b className="tabular-nums">{formatMoney(total, currency)}</b>.
      </p>

      {BUCKETS.map((bucket) => {
        const group = lines.filter((line) => line.bucket === bucket);
        if (group.length === 0) return null;
        return (
          <section key={bucket}>
            <h3 className="flex justify-between text-xs font-bold uppercase tracking-wide text-[var(--muted)]">
              <span>{DUE_BUCKET_LABEL[bucket]}</span>
              <span className="tabular-nums">{formatMoney(group.reduce((value, line) => value + line.remaining, 0), currency)}</span>
            </h3>
            <ul className="divide-y divide-[var(--line)]">
              {group.map((line) => (
                <DueRow
                  key={line.due.id}
                  line={line}
                  state={sent[line.due.id]}
                  busy={reminder.isPending}
                  onRemind={() => { setResult(null); setConfirming({ ids: [line.due.id], title: `${line.due.studentName} velisine hatırlatma gönderilsin mi?` }); }}
                />
              ))}
            </ul>
          </section>
        );
      })}

      {confirming && (
        <ConfirmBox
          title={confirming.title}
          rows={[{ label: "Kanal", value: "WhatsApp, hazır hatırlatma şablonu" }, { label: "Aidat", value: `${confirming.ids.length} kayıt` }]}
          note="Veli WhatsApp bildirimine izin vermediyse mesaj gitmez."
          confirmLabel={confirming.ids.length > 1 ? `${confirming.ids.length} hatırlatma gönder` : "Gönder"}
          pending={reminder.isPending}
          onConfirm={() => void sendReminders(confirming)}
          onCancel={() => setConfirming(null)}
        />
      )}
      {result && <DoneLine>{result}</DoneLine>}
      {error && <ErrorLine>{error}</ErrorLine>}

      <Actions>
        {!student && overdue.length > 1 && !confirming && (
          <button
            type="button"
            onClick={() => { setResult(null); setConfirming({ ids: overdue.map((line) => line.due.id), title: `Vadesi geçmiş ${overdue.length} aidat için velilere hatırlatma gönderilsin mi?` }); }}
            className="pressable inline-flex min-h-10 items-center rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)] hover:border-[var(--brand)]"
          >
            Vadesi geçenlere hatırlatma
          </button>
        )}
        <GoLink href="/dashboard/billing">Aidatlar ekranı</GoLink>
      </Actions>
    </AssistantCard>
  );
}

function DueRow({ line, state, busy, onRemind }: { line: DueLine; state?: PaymentReminderOutcome; busy: boolean; onRemind: () => void }) {
  const { due } = line;
  const collectHref = `/dashboard/billing?collect=${due.enrollmentId}&period=${encodeURIComponent(due.period)}&name=${encodeURIComponent(due.studentName)}`;
  return (
    <li className="space-y-2 py-2.5">
      <div className="flex items-start justify-between gap-3">
        <span className="min-w-0">
          <span className="block truncate text-sm font-bold">{due.studentName}</span>
          <span className="text-meta block text-xs">
            {due.instrumentName} · {COURSE_KIND_LABEL[due.courseKind]} · {formatPeriod(due.period)} · vade {formatDay(due.dueDate)}
            {due.discountReason ? ` · ${due.discountReason}` : ""}
          </span>
        </span>
        <span className="flex shrink-0 flex-col items-end gap-1">
          <span className="text-sm font-bold tabular-nums">{formatMoney(line.remaining, due.currency)}</span>
          <span className={`rounded-full px-2 py-0.5 text-[.7rem] font-bold ${line.bucket === "overdue" ? "bg-[var(--danger-soft)] text-[var(--danger-strong)]" : "bg-[var(--warning-soft)] text-[var(--warning-strong)]"}`}>
            {line.bucket === "overdue" ? "Ödenmedi" : "Yaklaşıyor"}
          </span>
        </span>
      </div>
      <div className="flex flex-wrap gap-2">
        {state
          ? <span className="text-meta text-xs font-semibold">{PAYMENT_REMINDER_OUTCOME_TEXT[state]}</span>
          : <button type="button" onClick={onRemind} disabled={busy} className="pressable inline-flex min-h-9 items-center rounded-xl border border-[var(--line)] bg-white px-3 text-xs font-bold text-[var(--brand-strong)] hover:border-[var(--brand)] disabled:opacity-50">Hatırlatma gönder</button>}
        <GoLink href={collectHref}>Ödeme al</GoLink>
      </div>
    </li>
  );
}
