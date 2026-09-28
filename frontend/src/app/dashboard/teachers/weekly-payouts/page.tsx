"use client";

import Link from "next/link";
import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { AdminGate, FormActions, FormMessage, Modal, Notice, PageHeader } from "@/components/ui";
import { errorMessage } from "@/lib/library";
import {
  formatDayMonth, formatPayWeek, payoutMoney, shiftWeekStart,
  useCreateTeacherPayout, useSetTeacherPayRate, useTeacherPayoutWeek,
  type TeacherPayoutWeek, type TeacherPayoutWeekRow,
} from "@/lib/teacher-payouts";

export default function TeacherWeeklyPayoutsPage() {
  return <AdminGate><WeeklyPayouts /></AdminGate>;
}

// Öğretmenlere haftalık ders ödemesi (docs/10-decisions.md O1). Kullanıcı kuralı: "her
// cumartesi tamamlanan derslerin ödemesini yapıyorum" - bu yüzden ekranın ekseni bir ödeme
// haftasıdır (Pazar → Cumartesi), ödeme günü de haftanın kapandığı cumartesidir.
//
// Ödeme kaydı Giderler defterine Maaş kategorisiyle düşer; ayrı bir öğretmen gideri defteri
// yok. Aynı iş için ikinci bir giriş noktası açılmadı (CLAUDE.md): Öğretmenler ekranı bu
// sayfaya bağlanır, ödeme yalnızca burada yapılır.
function WeeklyPayouts() {
  // null = "bugünü içeren hafta"; haftayı sunucu belirler, istemci yalnızca kaydırır.
  const [weekStart, setWeekStart] = useState<string | null>(null);
  const { data: week, isLoading, isError, refetch, isFetching } = useTeacherPayoutWeek(weekStart);
  const [payTarget, setPayTarget] = useState<TeacherPayoutWeekRow | null>(null);
  const [rateTarget, setRateTarget] = useState<TeacherPayoutWeekRow | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  function announce(text: string) {
    setNotice(text);
    window.setTimeout(() => setNotice((current) => (current === text ? null : current)), 5000);
  }

  function step(weeks: number) {
    if (!week) return;
    setWeekStart(shiftWeekStart(week.weekStart, weeks));
  }

  return (
    <div className="space-y-4">
      <PageHeader
        title="Haftalık öğretmen ödemesi"
        description="Ödeme haftası pazar başlar, cumartesi kapanır. Kaydedilen her ödeme Giderler defterine Maaş kategorisiyle yazılır."
        actions={<Link href="/dashboard/teachers" className="btn btn-quiet">Öğretmenler</Link>}
      />

      {notice && <Notice onDismiss={() => setNotice(null)}>{notice}</Notice>}

      <section className="app-card flex flex-wrap items-center gap-2 p-3" aria-label="Ödeme haftası seçimi">
        <button type="button" onClick={() => step(-1)} disabled={!week} className="btn btn-quiet min-h-11 px-3 disabled:opacity-50" aria-label="Önceki hafta">
          <Icon name="chevron" className="h-4 w-4 rotate-180" />
        </button>
        <p className="min-w-44 text-center text-sm font-bold">
          {week ? formatPayWeek(week.weekStart, week.weekEnd) : "Hafta yükleniyor…"}
        </p>
        <button type="button" onClick={() => step(1)} disabled={!week} className="btn btn-quiet min-h-11 px-3 disabled:opacity-50" aria-label="Sonraki hafta">
          <Icon name="chevron" className="h-4 w-4" />
        </button>
        {week && !week.isCurrentWeek && (
          <button type="button" onClick={() => setWeekStart(null)} className="btn btn-quiet min-h-11">Bu hafta</button>
        )}
        {week && (
          <p className="text-meta ml-auto">
            Ödeme günü <strong className="text-[var(--foreground)]">{formatDayMonth(week.payDay)} Cmt</strong>
            {!week.isClosed && " · hafta henüz kapanmadı"}
          </p>
        )}
      </section>

      {week && <WeekTotals week={week} />}

      <section className="app-card overflow-hidden">
        {isLoading && <div className="space-y-2 p-3">{Array.from({ length: 4 }, (_, index) => <div key={index} className="skeleton h-13 rounded-lg" />)}</div>}

        {!isLoading && isError && (
          <div className="grid min-h-40 place-items-center p-6 text-center">
            <div>
              <p className="text-sm font-bold">Hafta yüklenemedi</p>
              <button type="button" onClick={() => void refetch()} disabled={isFetching} className="btn btn-quiet mt-3 disabled:opacity-50">
                {isFetching ? "Yükleniyor…" : "Tekrar dene"}
              </button>
            </div>
          </div>
        )}

        {!isLoading && !isError && week && week.teachers.length === 0 && (
          <p className="p-6 text-center text-sm text-[var(--muted)]">Bu haftada ödeme yapılacak öğretmen yok.</p>
        )}

        {!isLoading && !isError && week && week.teachers.length > 0 && (
          <>
            <div className="hidden grid-cols-[minmax(0,1.2fr)_6rem_7rem_7rem_15.5rem] items-center gap-3 border-b border-[var(--line)] bg-[var(--surface-muted)]/40 px-3 py-2 text-micro text-[var(--muted)] md:grid">
              <span>Öğretmen</span>
              <span className="text-right">Tamamlanan</span>
              <span className="text-right">Ders başı</span>
              <span className="text-right">Tutar</span>
              <span className="text-right">Durum</span>
            </div>
            <ul className="divide-y divide-[var(--line)]">
              {week.teachers.map((row) => (
                <PayoutRow
                  key={row.teacherId}
                  row={row}
                  onPay={() => setPayTarget(row)}
                  onSetRate={() => setRateTarget(row)}
                />
              ))}
            </ul>
          </>
        )}
      </section>

      {week && payTarget && (
        <Modal
          open
          title="Haftalık ödemeyi gider olarak kaydet"
          description={`${payTarget.firstName} ${payTarget.lastName} · ${formatPayWeek(week.weekStart, week.weekEnd)}`}
          onClose={() => setPayTarget(null)}
          size="sm"
        >
          <PayoutForm
            week={week}
            row={payTarget}
            onClose={() => setPayTarget(null)}
            onPaid={(amount) => {
              setPayTarget(null);
              announce(`${payTarget.firstName} ${payTarget.lastName} için ${payoutMoney(amount, payTarget.currency)} gider olarak kaydedildi.`);
            }}
          />
        </Modal>
      )}

      {rateTarget && (
        <Modal
          open
          title="Ders başı ücret"
          description={`${rateTarget.firstName} ${rateTarget.lastName}`}
          onClose={() => setRateTarget(null)}
          size="sm"
        >
          <RateForm
            row={rateTarget}
            onClose={() => setRateTarget(null)}
            onSaved={(amount) => {
              setRateTarget(null);
              announce(`${rateTarget.firstName} ${rateTarget.lastName} için ders başı ücret ${payoutMoney(amount, rateTarget.currency)} olarak kaydedildi.`);
            }}
          />
        </Modal>
      )}
    </div>
  );
}

function WeekTotals({ week }: { week: TeacherPayoutWeek }) {
  return (
    <div className="grid gap-3 sm:grid-cols-3">
      <Stat label="Tamamlanan ders" value={`${week.totalCompletedLessons}`} tone="muted" />
      <Stat label="Ödenen" value={payoutMoney(week.paidTotal, week.currency)} tone="success" />
      <Stat
        label="Ödenecek"
        value={payoutMoney(week.payableTotal, week.currency)}
        secondary={week.isClosed ? undefined : "Hafta kapanmadan tutar değişebilir."}
        tone="warning"
      />
    </div>
  );
}

function Stat({ label, value, secondary, tone }: { label: string; value: string; secondary?: string; tone: "success" | "warning" | "muted" }) {
  const palette = { success: "text-[var(--success-strong)]", warning: "text-[var(--warning-strong)]", muted: "text-[var(--foreground)]" }[tone];
  return (
    <article className="app-card p-4">
      <p className="text-meta font-bold">{label}</p>
      <p className={`mt-2 text-xl font-bold tabular-nums ${palette}`}>{value}</p>
      {secondary && <p className="text-meta mt-1">{secondary}</p>}
    </article>
  );
}

function PayoutRow({ row, onPay, onSetRate }: { row: TeacherPayoutWeekRow; onPay: () => void; onSetRate: () => void }) {
  const paid = row.payout;
  return (
    <li className="grid min-h-14 grid-cols-[minmax(0,1fr)] items-center gap-2 px-3 py-2 md:grid-cols-[minmax(0,1.2fr)_6rem_7rem_7rem_15.5rem] md:gap-3">
      <div className="min-w-0">
        <p className="truncate text-sm font-bold">
          {row.firstName} {row.lastName}
          {row.status === "Inactive" && <span className="text-meta ml-2 font-semibold">· Pasif</span>}
        </p>
        <p className="text-meta mt-0.5 md:hidden">
          {row.completedLessons} tamamlanan ders
          {row.ratePerLesson !== null && ` · ders başı ${payoutMoney(row.ratePerLesson, row.currency)}`}
        </p>
      </div>

      <p className="text-meta hidden text-right tabular-nums md:block">
        <strong className="text-sm text-[var(--foreground)]">{row.completedLessons}</strong>
      </p>

      <p className="text-meta hidden text-right tabular-nums md:block">
        {row.ratePerLesson === null ? "—" : payoutMoney(row.ratePerLesson, row.currency)}
      </p>

      <p className="hidden text-right text-sm font-bold tabular-nums md:block">
        {paid ? payoutMoney(paid.amount, paid.currency) : row.computedAmount === null ? "—" : payoutMoney(row.computedAmount, row.currency)}
      </p>

      <div className="flex flex-wrap items-center justify-start gap-2 md:justify-end">
        {paid ? (
          <>
            <span className="rounded-full bg-[var(--success-soft)] px-2 py-1 text-[.75rem] font-bold text-[var(--success-strong)]">
              Ödendi · {formatDayMonth(paid.paidOn)}
            </span>
            <span className="text-meta tabular-nums md:hidden">{payoutMoney(paid.amount, paid.currency)}</span>
          </>
        ) : row.ratePerLesson === null ? (
          <button type="button" onClick={onSetRate} className="btn btn-quiet min-h-11">Ders başı ücreti gir</button>
        ) : row.completedLessons === 0 ? (
          <span className="text-meta">Bu hafta tamamlanan ders yok</span>
        ) : (
          <>
            <button type="button" onClick={onSetRate} className="btn btn-quiet min-h-11">Ücret</button>
            <button type="button" onClick={onPay} className="btn btn-primary min-h-11">Gider oluştur</button>
          </>
        )}
      </div>
    </li>
  );
}

function PayoutForm({ week, row, onClose, onPaid }: {
  week: TeacherPayoutWeek;
  row: TeacherPayoutWeekRow;
  onClose: () => void;
  onPaid: (amount: number) => void;
}) {
  const createPayout = useCreateTeacherPayout();
  const computed = row.computedAmount ?? 0;
  const [amount, setAmount] = useState(String(computed));
  const [paidOn, setPaidOn] = useState(week.payDay);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const parsedAmount = Number(amount.replace(",", "."));
  const amountChanged = Number.isFinite(parsedAmount) && parsedAmount !== computed;

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) {
      setError("Tutar pozitif bir sayı olmalı.");
      return;
    }
    try {
      await createPayout.mutateAsync({
        teacherId: row.teacherId,
        weekStart: week.weekStart,
        // Sunucu tutarı kendisi hesaplar; bunlar yalnızca ekranda görülenin teyididir
        // (arada bir derse yoklama girildiyse ödeme sessizce başka tutarla geçmesin).
        expectedLessonCount: row.completedLessons,
        expectedAmount: computed,
        agreedAmount: amountChanged ? parsedAmount : undefined,
        paidOn,
        note: note.trim() || undefined,
      });
      onPaid(parsedAmount);
    } catch (err) {
      // Sunucu doğrulama hatasını ProblemDetails.errors içinde taşır - kullanıcıya genel
      // "Bir hata oluştu" yerine asıl sebebi göster (örn. sayı ekrandakinden farklı).
      setError(errorMessage(err, "Ödeme kaydedilemedi."));
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/40 p-3">
        <p className="text-sm font-semibold tabular-nums">
          {row.completedLessons} tamamlanan ders × {payoutMoney(row.ratePerLesson ?? 0, row.currency)} = <strong>{payoutMoney(computed, row.currency)}</strong>
        </p>
        <p className="text-meta mt-1">
          Yalnızca yoklaması girilmiş (tamamlanmış) dersler sayılır; iptal ve ertelenmiş dersler sayılmaz.
        </p>
      </div>

      <label className="form-label">
        Ödenecek tutar (₺)
        <input
          type="number" min="0.01" step="0.01" required inputMode="decimal"
          value={amount} onChange={(event) => { setAmount(event.target.value); setError(null); }}
          className="field text-sm"
        />
        {amountChanged && (
          <span className="text-meta mt-1 block">
            Hesaplanan tutardan farklı; ikisi de ödeme kaydında ve denetim kaydında saklanır.
          </span>
        )}
      </label>

      <label className="form-label">
        Ödeme tarihi
        <input type="date" required value={paidOn} onChange={(event) => setPaidOn(event.target.value)} className="field text-sm" />
      </label>

      <label className="form-label">
        Not (isteğe bağlı)
        <input value={note} onChange={(event) => setNote(event.target.value)} maxLength={200} placeholder="Örn. elden ödendi" className="field text-sm" />
      </label>

      {error && <FormMessage tone="error">{error}</FormMessage>}

      <FormActions onCancel={onClose} submitLabel="Gideri kaydet" pending={createPayout.isPending} pendingLabel="Kaydediliyor…" />
    </form>
  );
}

function RateForm({ row, onClose, onSaved }: { row: TeacherPayoutWeekRow; onClose: () => void; onSaved: (amount: number) => void }) {
  const setRate = useSetTeacherPayRate();
  const [amount, setAmount] = useState(row.ratePerLesson === null ? "" : String(row.ratePerLesson));
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    const parsed = Number(amount.replace(",", "."));
    if (!Number.isFinite(parsed) || parsed <= 0) {
      setError("Ders başı ücret pozitif bir sayı olmalı.");
      return;
    }
    try {
      await setRate.mutateAsync({ teacherId: row.teacherId, amountPerLesson: parsed, currency: row.currency });
      onSaved(parsed);
    } catch (err) {
      setError(errorMessage(err, "Ücret kaydedilemedi."));
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3.5">
      <label className="form-label">
        Ders başı ücret (₺)
        <input
          type="number" min="0.01" step="0.01" required autoFocus inputMode="decimal"
          value={amount} onChange={(event) => { setAmount(event.target.value); setError(null); }}
          className="field text-sm"
        />
      </label>
      <p className="text-meta">
        Ücret değişince yalnızca bundan sonraki ödemeler etkilenir; ödenmiş haftalar kendi tutarını korur.
      </p>

      {error && <FormMessage tone="error">{error}</FormMessage>}

      <FormActions onCancel={onClose} submitLabel="Ücreti kaydet" pending={setRate.isPending} pendingLabel="Kaydediliyor…" />
    </form>
  );
}
