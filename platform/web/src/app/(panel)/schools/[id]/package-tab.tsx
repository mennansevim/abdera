"use client";

import { useState } from "react";
import { ConfirmDialog, FormMessage, Panel, StatStrip } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { formatMoney, formatNumber } from "@/lib/format";
import { useReleaseModules } from "@/lib/releases";
import { isRunOpen, useSchoolPackage, type SchoolDetail } from "@/lib/schools";

const MAX_STAFF_SEATS = 1000;

function parse(value: string): number {
  const normalized = value.trim().replace(",", ".");
  return normalized === "" ? Number.NaN : Number(normalized);
}

const isMoney = (value: number) => Number.isFinite(value) && value >= 0 && value <= 1_000_000 && Math.round(value * 100) === value * 100;

// Kullanıcı paketi (ARCHITECTURE.md §5.7): personel hesabı sınırı ve yıllık fiyat. Sınır değişince
// okulun yapılandırması yeniden yazılır (~30 sn kesinti); yalnızca fiyat değişirse okula dokunulmaz.
// Eklenti fiyatı yalnızca açık eklentiler için girilir; eklenti Modüller sekmesinden açılıp kapatılır.
export function PackageTab({ detail, onQueued }: { detail: SchoolDetail; onQueued: (message: string) => void }) {
  const pkg = detail.school.package;
  const catalog = useReleaseModules(detail.school.appVersion);
  const save = useSchoolPackage(detail.school.id);
  const [seats, setSeats] = useState(() => (pkg?.staffSeats ? String(pkg.staffSeats) : ""));
  const [perSeat, setPerSeat] = useState(() => (pkg?.pricePerSeat !== null && pkg?.pricePerSeat !== undefined ? String(pkg.pricePerSeat) : ""));
  const [addonPrices, setAddonPrices] = useState<Record<string, string>>(() =>
    Object.fromEntries(detail.modules.map((key) => [key, pkg?.addonPrices[key] !== undefined ? String(pkg.addonPrices[key]) : "0"])));
  const [confirm, setConfirm] = useState(false);

  const currency = pkg?.currency ?? "TRY";
  const used = detail.school.telemetry?.activeStaffUsers ?? null;
  const seatCount = parse(seats);
  const perSeatValue = parse(perSeat);
  const errors: Record<string, string> = {};
  if (!Number.isInteger(seatCount) || seatCount < 1 || seatCount > MAX_STAFF_SEATS) errors.seats = `1 ile ${MAX_STAFF_SEATS} arasında bir tam sayı.`;
  if (!isMoney(perSeatValue)) errors.perSeat = "Fiyatı gir (en fazla iki ondalık).";
  for (const key of detail.modules) if (!isMoney(parse(addonPrices[key] ?? ""))) errors[key] = "Fiyatı gir (ücretsizse 0).";
  const valid = Object.keys(errors).length === 0;

  const seatsChanged = valid && seatCount !== pkg?.staffSeats;
  const pricesChanged = valid && (perSeatValue !== pkg?.pricePerSeat || detail.modules.some((key) => parse(addonPrices[key] ?? "") !== (pkg?.addonPrices[key] ?? Number.NaN)));
  const busy = detail.runs.some((run) => isRunOpen(run.status));
  const seatsEditable = detail.school.status === "Active" && !busy;
  const preview = valid ? Math.round((seatCount * perSeatValue + detail.modules.reduce((sum, key) => sum + parse(addonPrices[key] ?? "0"), 0)) * 100) / 100 : null;
  const name = (key: string) => catalog.data?.find((m) => m.key === key)?.name ?? key;

  function submit() {
    save.mutate(
      { staffSeats: seatCount, pricePerSeat: perSeatValue, addonPrices: Object.fromEntries(detail.modules.map((key) => [key, parse(addonPrices[key] ?? "0")])) },
      {
        onSuccess: (result) => {
          setConfirm(false);
          onQueued(result.runId ? "Paket kaydedildi; kullanıcı sınırı için okul ~30 sn yeniden başlayacak." : "Paket fiyatı kaydedildi.");
        },
      },
    );
  }

  return (
    <div className="grid gap-4">
      <StatStrip
        label="Paket"
        items={[
          { key: "seats", label: "Kullanıcı", value: pkg?.staffSeats ? `${formatNumber(used)} / ${formatNumber(pkg.staffSeats)}` : "Sınırsız", tone: used !== null && pkg?.staffSeats && used >= pkg.staffSeats ? "warning" : undefined, hint: "etkin yönetici + öğretmen hesabı" },
          { key: "total", label: "Yıllık tutar", value: formatMoney(pkg?.yearlyTotal ?? 0, currency) },
        ]}
      />
      {!pkg?.staffSeats && <FormMessage tone="info">Bu okulun paketi yok; kullanıcı sayısı sınırsız. Paket kaydedilince okulda sınır başlar.</FormMessage>}
      {used !== null && valid && seatCount < used && (
        <FormMessage tone="error">Okulda şu an {used} etkin personel hesabı var. Sınırı {seatCount}’a indirmek var olan hesapları kapatmaz, yalnızca yeni hesap açılmasını engeller.</FormMessage>
      )}

      <Panel title="Kullanıcı paketi" meta="Veli ve öğrenci sayılmaz">
        <div className="grid gap-4 sm:grid-cols-2">
          <label className="form-label">
            Kullanıcı sayısı
            <input className="form-input" inputMode="numeric" value={seats} onChange={(event) => setSeats(event.target.value)} disabled={!seatsEditable} />
            {errors.seats ? <span className="form-error">{errors.seats}</span> : !seatsEditable && <span className="text-meta">Yalnızca çalışan ve süren işlemi olmayan okulda değişir.</span>}
          </label>
          <label className="form-label">
            Kullanıcı başı yıllık fiyat ({currency})
            <input className="form-input" inputMode="decimal" value={perSeat} onChange={(event) => setPerSeat(event.target.value)} />
            {errors.perSeat && <span className="form-error">{errors.perSeat}</span>}
          </label>
          {detail.modules.map((key) => (
            <label key={key} className="form-label">
              {name(key)} ({currency}/ay)
              <input className="form-input" inputMode="decimal" value={addonPrices[key] ?? ""} onChange={(event) => setAddonPrices((current) => ({ ...current, [key]: event.target.value }))} />
              {errors[key] && <span className="form-error">{errors[key]}</span>}
            </label>
          ))}
        </div>
        <div className="mt-4 flex flex-wrap items-center justify-between gap-3 border-t border-[var(--line)] pt-3">
          <span className="text-meta">Yeni yıllık tutar: <b className="text-[var(--foreground)]">{preview === null ? "—" : formatMoney(preview, currency)}</b></span>
          <button type="button" className="btn btn-primary" disabled={!valid || (!seatsChanged && !pricesChanged)} onClick={() => { save.reset(); setConfirm(true); }}>
            Paketi kaydet
          </button>
        </div>
      </Panel>

      <ConfirmDialog
        open={confirm}
        title="Paketi kaydet"
        description={detail.school.displayName}
        confirmLabel="Kaydet"
        pending={save.isPending}
        error={save.error ? errorMessage(save.error) : null}
        onClose={() => setConfirm(false)}
        onConfirm={submit}
      >
        <p className="text-body">
          {seatsChanged
            ? `Kullanıcı sınırı ${pkg?.staffSeats ?? "sınırsız"} → ${seatCount} olacak. Okulun yapılandırması yeniden yazılır, api/web ~30 sn yeniden başlar.`
            : "Yalnızca fiyat değişiyor; okula dokunulmaz."}
        </p>
      </ConfirmDialog>
    </div>
  );
}
