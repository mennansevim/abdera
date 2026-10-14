"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { MonthInput } from "@/components/month-input";
import { fromIsoDate, relativeWeekName } from "@/components/week-picker";
import { FormActions, FormMessage } from "@/components/ui";
import { errorMessage } from "@/lib/library";
import {
  formatDayMonth, formatPayWeek, payoutMoney, shiftWeekStart,
  useCreateTeacherGroupPayout, useCreateTeacherPayout, useSetTeacherPayRate, useTeacherGroupMonth, useTeacherPayoutWeek,
  type TeacherGroupMonthRow, type TeacherPayoutWeekRow,
} from "@/lib/teacher-payouts";

// "Gider ekle" formunun "Haftalık" sekmesi: bir öğretmene bir ödeme haftasının (pazartesi →
// cumartesi dahil, docs/10-decisions.md O1) tamamlanan dersleri için ödeme. Öğretmen haftalık
// ödemesinin TEK giriş noktası budur; Öğretmenler listesindeki "Ödeme yap" da Giderler
// ekranını bu sekme, öğretmen ve hafta seçili olarak açar - ayrı bir form değildir.
//
// Tutarı sunucu hesaplar (TeacherPayRate.ComputeWeeklyAmount); burada gösterilen hesap yalnızca
// teyit için gönderilir. Yöneticinin değiştirdiği tutar `agreedAmount` olarak gider ve audit'e
// hesaplananla birlikte yazılır.

const pad = (value: number) => String(value).padStart(2, "0");

// Varsayılan hafta: en son kapanan (ya da bugün kapanan) ödeme haftası - ödeme cumartesi
// yapılır, hafta içinde açılan form çoğunlukla geçen haftayı ödemek içindir. Sunucu verilen
// günü içeren haftaya normalize eder.
export function lastPayDay(today = new Date()): string {
  const date = new Date(today);
  date.setDate(date.getDate() - ((date.getDay() + 1) % 7));
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}`;
}

const parseAmount = (text: string) => Number(text.replace(",", "."));
const computeAmount = (lessons: number, rate: number) => Math.round(lessons * rate * 100) / 100;

export function TeacherPayoutForm({ initialTeacherId, initialWeek, onClose }: { initialTeacherId?: string; initialWeek?: string; onClose: () => void }) {
  const [week, setWeek] = useState(initialWeek ?? lastPayDay());
  const [teacherId, setTeacherId] = useState(initialTeacherId ?? "");
  const { data: payWeek, isLoading, isError } = useTeacherPayoutWeek(week);
  const row = payWeek?.teachers.find((item) => item.teacherId === teacherId) ?? null;

  return (
    <div className="space-y-3.5">
      <label className="form-label">Öğretmen
        <select value={teacherId} onChange={(event) => setTeacherId(event.target.value)} required className="field text-sm">
          <option value="">Öğretmen seç</option>
          {(payWeek?.teachers ?? []).map((item) => (
            <option key={item.teacherId} value={item.teacherId}>
              {item.firstName} {item.lastName}{item.payout ? " · ödendi" : item.completedLessons > 0 ? ` · ${item.completedLessons} ders` : ""}
            </option>
          ))}
        </select>
      </label>

      <div className="form-label">Ödeme haftası (Pzt–Cmt)
        <div className="flex items-center gap-1.5">
          <button type="button" onClick={() => payWeek && setWeek(shiftWeekStart(payWeek.weekStart, -1))} disabled={!payWeek} className="icon-btn icon-btn-quiet" aria-label="Önceki hafta"><Icon name="arrow-left" className="h-4 w-4" /></button>
          <span aria-live="polite" className="field flex flex-1 items-center justify-center text-sm font-semibold tabular-nums">
            {payWeek ? <>{relativeWeekName(fromIsoDate(payWeek.weekStart)) && <strong className="mr-1.5">{relativeWeekName(fromIsoDate(payWeek.weekStart))} ·</strong>}{formatPayWeek(payWeek.weekStart, payWeek.weekEnd)}</> : "Yükleniyor…"}
          </span>
          <button type="button" onClick={() => payWeek && setWeek(shiftWeekStart(payWeek.weekStart, 1))} disabled={!payWeek} className="icon-btn icon-btn-quiet" aria-label="Sonraki hafta"><Icon name="arrow-right" className="h-4 w-4" /></button>
        </div>
      </div>

      {isError && <FormMessage tone="error">Hafta yüklenemedi.</FormMessage>}
      {!isLoading && payWeek && !row && (
        <>
          <p className="text-meta">{teacherId ? "Bu öğretmen seçilen haftada listede yok." : "Tamamlanan ders sayısı ve tutar öğretmen seçilince gelir."}</p>
          <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" disabled />
        </>
      )}
      {payWeek && row && (
        // Öğretmen/hafta değişince tutar ve ücret alanları yeniden hesaplanarak dolar.
        <PayoutFields key={`${row.teacherId}:${payWeek.weekStart}`} row={row} weekStart={payWeek.weekStart} payDay={payWeek.payDay} onClose={onClose} />
      )}
    </div>
  );
}

function PayoutFields({ row, weekStart, payDay, onClose }: { row: TeacherPayoutWeekRow; weekStart: string; payDay: string; onClose: () => void }) {
  const createPayout = useCreateTeacherPayout();
  const setRate = useSetTeacherPayRate();
  const [rate, setRateText] = useState(row.ratePerLesson === null ? "" : String(row.ratePerLesson));
  const [amount, setAmount] = useState(row.computedAmount === null ? "" : String(row.computedAmount));
  // Tutar elle değiştirilmediği sürece ücret alanıyla birlikte yeniden hesaplanır.
  const [amountEdited, setAmountEdited] = useState(false);
  const [paidOn, setPaidOn] = useState(payDay);
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);

  const paid = row.payout;
  const parsedRate = parseAmount(rate);
  const rateValid = Number.isFinite(parsedRate) && parsedRate > 0;
  const computed = rateValid ? computeAmount(row.completedLessons, parsedRate) : null;
  const parsedAmount = parseAmount(amount);
  const rateChanged = rateValid && parsedRate !== row.ratePerLesson;
  const amountDiffers = computed !== null && Number.isFinite(parsedAmount) && parsedAmount !== computed;

  if (paid) {
    return (
      <>
        <FormMessage tone="success">
          {row.firstName} {row.lastName} için bu haftanın ödemesi yapıldı: {payoutMoney(paid.amount, paid.currency)} ({paid.lessonCount} ders, {formatDayMonth(paid.paidOn)}). Ödeme kayıtları silinmez; düzeltme gerekiyorsa karşı kayıt gir.
        </FormMessage>
        <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" disabled />
      </>
    );
  }

  if (row.completedLessons === 0) {
    return (
      <>
        <p className="text-meta rounded-xl bg-[var(--surface-muted)] px-3 py-2">Bu haftada tamamlanmış ders yok; ödenecek bir tutar çıkmıyor. Yalnızca yoklaması girilmiş dersler sayılır.</p>
        <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" disabled />
      </>
    );
  }

  function changeRate(text: string) {
    setRateText(text);
    setError(null);
    const value = parseAmount(text);
    if (!amountEdited && Number.isFinite(value) && value > 0) setAmount(String(computeAmount(row.completedLessons, value)));
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!rateValid || computed === null) return setError("Ders başı ücret pozitif bir sayı olmalı.");
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) return setError("Tutar pozitif bir sayı olmalı.");
    try {
      // Ücret değiştiyse önce öğretmenin ücreti güncellenir; sunucu tutarı yeni ücretle hesaplar.
      if (rateChanged) await setRate.mutateAsync({ teacherId: row.teacherId, amountPerLesson: parsedRate, currency: row.currency });
      await createPayout.mutateAsync({
        teacherId: row.teacherId,
        weekStart,
        expectedLessonCount: row.completedLessons,
        expectedAmount: computed,
        agreedAmount: amountDiffers ? parsedAmount : undefined,
        paidOn,
        note: note.trim() || undefined,
      });
      onClose();
    } catch (err) {
      setError(errorMessage(err, "Ödeme kaydedilemedi."));
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3.5">
      <label className="form-label">Ders başı ücret (₺)
        <input type="number" inputMode="decimal" min={0.01} step={0.01} value={rate} onChange={(event) => changeRate(event.target.value)} required className="field text-sm" placeholder="Örn. 750" />
        {row.ratePerLesson === null
          ? <span className="text-meta mt-1 block">Bu öğretmenin ücreti henüz girilmemiş; kaydedince sonraki haftalar için de geçerli olur.</span>
          : rateChanged && <span className="text-meta mt-1 block">Öğretmenin ücreti {payoutMoney(row.ratePerLesson, row.currency)} → {payoutMoney(parsedRate, row.currency)} olarak güncellenir; ödenmiş haftalar etkilenmez.</span>}
      </label>

      <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/40 p-3">
        <p className="text-sm font-semibold tabular-nums">
          {row.completedLessons} tamamlanan ders × {rateValid ? payoutMoney(parsedRate, row.currency) : "—"} = <strong>{computed === null ? "—" : payoutMoney(computed, row.currency)}</strong>
        </p>
        <p className="text-meta mt-1">Yalnızca yoklaması girilmiş (tamamlanmış) dersler sayılır; iptal ve ertelenmiş dersler sayılmaz. Grup dersleri (Resim) burada sayılmaz, Aylık grup sekmesinden ödenir.</p>
      </div>

      <label className="form-label">Ödenecek tutar (₺)
        <input type="number" inputMode="decimal" min={0.01} step={0.01} value={amount} onChange={(event) => { setAmount(event.target.value); setAmountEdited(true); setError(null); }} required className="field text-sm" />
        {amountDiffers && (
          <span className="text-meta mt-1 block">
            Hesaplanan tutardan farklı; ikisi de ödeme kaydında saklanır.{" "}
            <button type="button" onClick={() => { setAmount(String(computed)); setAmountEdited(false); }} className="font-bold text-[var(--brand)] hover:underline">Hesaplanana dön</button>
          </span>
        )}
      </label>

      <label className="form-label">Ödeme tarihi
        <input type="date" required value={paidOn} min={weekStart} onChange={(event) => setPaidOn(event.target.value)} className="field text-sm" />
      </label>

      <label className="form-label">Not <span className="font-normal text-[var(--muted)]">(isteğe bağlı)</span>
        <input value={note} onChange={(event) => setNote(event.target.value)} maxLength={200} placeholder="Örn. elden ödendi" className="field text-sm" />
      </label>

      <p className="text-meta">Ödeme Giderler defterine Maaş kategorisiyle yazılır.</p>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" pending={createPayout.isPending || setRate.isPending} pendingLabel="Kaydediliyor…" />
    </form>
  );
}

// "Gider ekle" formunun "Aylık grup" sekmesi (docs/10-decisions.md AC): grup dersi (Resim)
// öğretmenine, seçilen ayın tahakkuk eden grup aidatlarının %40'ı. Grup dersleri haftalık
// sekmede sayılmaz - grup öğretmeninin tek ödeme yolu bu sekmedir.
const MONTH_NAMES = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
const formatPeriod = (period: string) => {
  const [year, month] = period.split("-").map(Number);
  return `${MONTH_NAMES[month - 1]} ${year}`;
};
const lastDayOf = (period: string) => {
  const [year, month] = period.split("-").map(Number);
  return `${year}-${pad(month)}-${pad(new Date(year, month, 0).getDate())}`;
};
const todayIso = () => {
  const now = new Date();
  return `${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}`;
};

export function TeacherGroupPayoutForm({ onClose }: { onClose: () => void }) {
  // Varsayılan ay: geçen ay - grup payı ay kapanınca ödenir.
  const [period, setPeriod] = useState(() => {
    const date = new Date();
    date.setDate(1);
    date.setMonth(date.getMonth() - 1);
    return `${date.getFullYear()}-${pad(date.getMonth() + 1)}`;
  });
  const [teacherId, setTeacherId] = useState("");
  const { data: month, isLoading, isError } = useTeacherGroupMonth(period);
  const row = month?.teachers.find((item) => item.teacherId === teacherId) ?? null;

  return (
    <div className="space-y-3.5">
      <label className="form-label">Ay
        <MonthInput value={period} onChange={(value) => setPeriod(value)} label="Ödeme ayı" />
      </label>
      <label className="form-label">Öğretmen
        <select value={teacherId} onChange={(event) => setTeacherId(event.target.value)} required className="field text-sm">
          <option value="">{month && !month.teachers.length ? "Grup dersi veren öğretmen yok" : "Öğretmen seç"}</option>
          {(month?.teachers ?? []).map((item) => (
            <option key={item.teacherId} value={item.teacherId}>
              {item.firstName} {item.lastName}{item.payout ? " · ödendi" : item.revenueBase > 0 ? ` · ${payoutMoney(item.computedAmount, item.currency)}` : ""}
            </option>
          ))}
        </select>
      </label>

      {isError && <FormMessage tone="error">Ay yüklenemedi.</FormMessage>}
      {!isLoading && month && !row && (
        <>
          <p className="text-meta">Grup dersi öğretmenine, seçilen ayın grup aidatlarının %{month.sharePercent}&apos;ı ödenir. Tutar öğretmen seçilince gelir.</p>
          <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" disabled />
        </>
      )}
      {month && row && <GroupPayoutFields key={`${row.teacherId}:${month.period}`} row={row} period={month.period} onClose={onClose} />}
    </div>
  );
}

function GroupPayoutFields({ row, period, onClose }: { row: TeacherGroupMonthRow; period: string; onClose: () => void }) {
  const createPayout = useCreateTeacherGroupPayout();
  const [amount, setAmount] = useState(String(row.computedAmount));
  const lastDay = lastDayOf(period);
  const [paidOn, setPaidOn] = useState(lastDay < todayIso() ? lastDay : todayIso());
  const [note, setNote] = useState("");
  const [error, setError] = useState<string | null>(null);
  const parsedAmount = parseAmount(amount);
  const amountDiffers = Number.isFinite(parsedAmount) && parsedAmount !== row.computedAmount;

  if (row.payout) {
    return (
      <>
        <FormMessage tone="success">
          {row.firstName} {row.lastName} için {formatPeriod(period)} grup payı ödendi: {payoutMoney(row.payout.amount, row.payout.currency)} ({formatDayMonth(row.payout.paidOn)}). Ödeme kayıtları silinmez; düzeltme gerekiyorsa karşı kayıt gir.
        </FormMessage>
        <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" disabled />
      </>
    );
  }

  if (row.revenueBase <= 0) {
    return (
      <>
        <p className="text-meta rounded-xl bg-[var(--surface-muted)] px-3 py-2">{formatPeriod(period)} için bu öğretmenin grup aidatı yok; ödenecek bir tutar çıkmıyor.</p>
        <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" disabled />
      </>
    );
  }

  async function submit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (!Number.isFinite(parsedAmount) || parsedAmount <= 0) return setError("Tutar pozitif bir sayı olmalı.");
    try {
      await createPayout.mutateAsync({
        teacherId: row.teacherId,
        period,
        expectedRevenueBase: row.revenueBase,
        expectedAmount: row.computedAmount,
        agreedAmount: amountDiffers ? parsedAmount : undefined,
        paidOn,
        note: note.trim() || undefined,
      });
      onClose();
    } catch (err) {
      setError(errorMessage(err, "Ödeme kaydedilemedi."));
    }
  }

  return (
    <form onSubmit={submit} className="space-y-3.5">
      <div className="rounded-xl border border-[var(--line)] bg-[var(--surface-muted)]/40 p-3">
        <p className="text-sm font-semibold tabular-nums">
          {formatPeriod(period)} grup geliri {payoutMoney(row.revenueBase, row.currency)} × %{row.sharePercent} = <strong>{payoutMoney(row.computedAmount, row.currency)}</strong>
        </p>
        <p className="text-meta mt-1">{row.receivableCount} öğrencinin bu ay açılan aidatı (indirim sonrası, ödenmiş olsun olmasın). İptal edilen aidat sayılmaz.</p>
      </div>

      <label className="form-label">Ödenecek tutar (₺)
        <input type="number" inputMode="decimal" min={0.01} step={0.01} value={amount} onChange={(event) => { setAmount(event.target.value); setError(null); }} required className="field text-sm" />
        {amountDiffers && (
          <span className="text-meta mt-1 block">
            Hesaplanan tutardan farklı; ikisi de ödeme kaydında saklanır.{" "}
            <button type="button" onClick={() => setAmount(String(row.computedAmount))} className="font-bold text-[var(--brand)] hover:underline">Hesaplanana dön</button>
          </span>
        )}
      </label>

      <label className="form-label">Ödeme tarihi
        <input type="date" required value={paidOn} min={`${period}-01`} onChange={(event) => setPaidOn(event.target.value)} className="field text-sm" />
      </label>

      <label className="form-label">Not <span className="font-normal text-[var(--muted)]">(isteğe bağlı)</span>
        <input value={note} onChange={(event) => setNote(event.target.value)} maxLength={200} placeholder="Örn. elden ödendi" className="field text-sm" />
      </label>

      <p className="text-meta">Ödeme Giderler defterine Maaş kategorisiyle yazılır.</p>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Ödemeyi kaydet" pending={createPayout.isPending} pendingLabel="Kaydediliyor…" />
    </form>
  );
}
