"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { BrandMark, Icon, instrumentBadgeStyle } from "@/components/icons";
import { useMe } from "@/lib/use-auth";
import {
  SHOW_ITEM_KIND_LABEL,
  studentPhotoUrl,
  useStage,
  useStageControl,
  type StageItem,
} from "@/lib/shows";

// Gösteri gecesinin canlı ekranı. Run-of-show araçlarının standart yüzeyi:
//
//   ŞU AN  - sahnedeki öğrenci ve çaldığı eser, ekranın tamamını kaplayacak büyüklükte
//            (kullanıcı isteği: "şu an çalacağı eser büyük punto ile gösterilebilir")
//   SIRADAKİ / ONDAN SONRAKİ - kulisteki görevli bir sonraki öğrenciyi sahne kenarına
//            çağırabilsin diye iki adım ileri görünür
//   Tek tuşla ilerletme - boşluk veya sağ ok; yanlış basılırsa sol ok geri alır
//
// İşaretçi sunucuda olduğu için sahnedeki ekran, kulis tableti ve yöneticinin dizüstü
// aynı anı gösterir (bkz. ShowStage.cs). Bu sayfa yalnızca onu okur ve komut gönderir.

function ProgressBar({ value, total }: { value: number; total: number }) {
  const percent = total > 0 ? Math.min(100, Math.round((value / total) * 100)) : 0;
  return (
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-white/15" role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={total}>
      <div className="h-full rounded-full bg-[var(--brand)] transition-[width] duration-500" style={{ width: `${percent}%` }} />
    </div>
  );
}

function UpNextCard({ label, item }: { label: string; item: StageItem | null }) {
  return (
    <article className="min-w-0 flex-1 rounded-2xl border border-white/10 bg-white/5 p-4">
      <p className="text-[.7rem] font-bold uppercase tracking-[.18em] text-white/40">{label}</p>
      {!item && <p className="mt-2 text-sm text-white/40">—</p>}
      {item && (
        <div className="mt-2 flex items-center gap-3">
          <span className="grid h-12 w-12 shrink-0 place-items-center overflow-hidden rounded-xl bg-white/10 text-sm font-bold text-white/70">
            {item.hasPhoto && item.studentId
              /* eslint-disable-next-line @next/next/no-img-element */
              ? <img src={studentPhotoUrl(item.studentId, item.photoVersion)} alt="" className="h-full w-full object-cover" />
              : item.kind === "Performance"
                ? (item.studentName ?? "?").split(" ").map((part) => part[0]).slice(0, 2).join("")
                : <Icon name={item.kind === "Intermission" ? "clock" : "bell"} className="h-4 w-4" />}
          </span>
          <div className="min-w-0">
            <p className="truncate text-base font-bold text-white">
              {item.kind === "Performance" ? item.studentName : (item.pieceTitle ?? SHOW_ITEM_KIND_LABEL[item.kind])}
            </p>
            <p className="truncate text-sm text-white/55">
              {item.kind === "Performance"
                ? `${item.pieceTitle ?? ""}${item.composer ? ` · ${item.composer}` : ""}`
                : (item.note ?? "")}
            </p>
          </div>
        </div>
      )}
    </article>
  );
}

export default function StagePage() {
  const params = useParams<{ showId: string }>();
  const showId = params.showId;
  const { data: me } = useMe();
  const { data: stage, isLoading } = useStage(showId);
  const control = useStageControl(showId);
  const [error, setError] = useState<string | null>(null);
  const isAdmin = me?.role === "Admin";
  const isLive = stage?.status === "Live";

  const send = useCallback(
    (command: Parameters<typeof control.mutate>[0]["command"], itemId?: string) => {
      setError(null);
      control.mutate({ command, itemId }, {
        onError: (err) => setError(err instanceof Error ? err.message : "İşlem yapılamadı."),
      });
    },
    [control],
  );

  // Tek tuşla ilerletme - gösteri gecesinde fare aramak için zaman yok. Bir metin
  // kutusuna yazılırken devreye girmemesi için hedef kontrol edilir.
  useEffect(() => {
    if (!isAdmin || !isLive) return;
    function onKeyDown(event: KeyboardEvent) {
      const target = event.target as HTMLElement | null;
      if (target && ["INPUT", "TEXTAREA", "SELECT"].includes(target.tagName)) return;
      if (event.key === " " || event.key === "ArrowRight") {
        event.preventDefault();
        send("advance");
      } else if (event.key === "ArrowLeft") {
        event.preventDefault();
        send("back");
      }
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [isAdmin, isLive, send]);

  if (isLoading) return <div className="skeleton h-[70vh] rounded-3xl" />;
  if (!stage) return <p className="app-card p-6 text-sm text-[var(--muted)]">Gösteri bulunamadı.</p>;

  const current = stage.current;
  const behindMinutes = stage.elapsedMinutes - stage.totalDurationMinutes;

  return (
    <div className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <Link href={`/dashboard/shows/${showId}`} className="btn btn-quiet"><Icon name="arrow-left" className="h-4 w-4" />Program</Link>
        <div className="flex flex-wrap items-center gap-2">
          {stage.status === "Live" && (
            <span className="inline-flex items-center gap-1.5 rounded-full bg-[var(--danger-soft)] px-2.5 py-1 text-[.75rem] font-bold text-[var(--danger-strong)]">
              <span className="h-1.5 w-1.5 animate-pulse rounded-full bg-current" />Sahnede
            </span>
          )}
          <button
            type="button"
            onClick={() => {
              const root = document.getElementById("stage-screen");
              if (!document.fullscreenElement) void root?.requestFullscreen?.();
              else void document.exitFullscreen();
            }}
            className="btn btn-quiet"
          >
            Tam ekran
          </button>
        </div>
      </div>

      <section id="stage-screen" className="overflow-hidden rounded-3xl bg-[#1a1512] p-5 text-white sm:p-8">
        <header className="flex flex-wrap items-baseline justify-between gap-2">
          <h1 className="font-serif text-lg font-bold">{stage.title}</h1>
          <p className="text-sm tabular-nums text-white/50">
            {stage.completedItems}/{stage.totalItems} sıra
            {stage.startedAt && <> · {stage.elapsedMinutes} dk geçti</>}
            {stage.startedAt && stage.totalDurationMinutes > 0 && (
              <> · <span className={behindMinutes > 5 ? "text-[var(--danger)]" : "text-white/50"}>
                {behindMinutes > 0 ? `${behindMinutes} dk gerideyiz` : `${Math.abs(behindMinutes)} dk öndeyiz`}
              </span></>
            )}
          </p>
        </header>

        <div className="mt-3"><ProgressBar value={stage.completedItems} total={stage.totalItems} /></div>

        {/* ŞU AN — ekranın asıl işi. */}
        <div className="mt-6 min-h-[16rem] sm:mt-8">
          {stage.status === "Draft" && (
            <div className="grid min-h-[16rem] place-items-center text-center">
              <div>
                <p className="font-serif text-2xl text-white/70">Gösteri henüz başlamadı</p>
                <p className="mt-2 text-sm text-white/40">
                  {stage.totalItems > 0
                    ? `Programda ${stage.totalItems} sıra hazır.`
                    : "Programda hiç sıra yok - önce program ekranından ekleyin."}
                </p>
              </div>
            </div>
          )}

          {stage.status === "Completed" && (
            <div className="grid min-h-[16rem] place-items-center text-center">
              <div>
                <p className="font-serif text-3xl text-white">Gösteri tamamlandı</p>
                <p className="mt-2 text-sm text-white/40">{stage.totalItems} sıra sahnelendi.</p>
              </div>
            </div>
          )}

          {isLive && !current && (
            <div className="grid min-h-[16rem] place-items-center text-center">
              <p className="font-serif text-2xl text-white/60">Sahne boş</p>
            </div>
          )}

          {isLive && current && current.kind !== "Performance" && (
            <div className="grid min-h-[16rem] place-items-center text-center">
              <div>
                <p className="text-[.7rem] font-bold uppercase tracking-[.2em] text-white/40">{SHOW_ITEM_KIND_LABEL[current.kind]}</p>
                <p className="mt-3 font-serif text-5xl font-bold leading-tight sm:text-7xl">{current.pieceTitle ?? SHOW_ITEM_KIND_LABEL[current.kind]}</p>
                {current.note && <p className="mt-3 text-lg text-white/50">{current.note}</p>}
              </div>
            </div>
          )}

          {isLive && current && current.kind === "Performance" && (
            <div className="relative overflow-hidden rounded-[2rem] bg-[linear-gradient(160deg,var(--sidebar-from)_0%,#c15a4a_45%,var(--sidebar-to)_100%)] p-5 sm:p-8">
              {/* Dekoratif blob'lar - şablonun renkli/organik zemin hissi. Veriye bağlı
                  değil, salt görsel doku; yüzde/blur tabanlı olduğu için ekran boyutundan
                  bağımsız çalışır (indirilmiş bir görsel yerine gerçek CSS - bkz. sohbet). */}
              <div aria-hidden className="pointer-events-none absolute -left-16 -top-16 h-56 w-56 rounded-full bg-white/10 blur-2xl" />
              <div aria-hidden className="pointer-events-none absolute -right-10 top-1/3 h-40 w-40 rounded-full bg-white/15 blur-2xl" />
              <div aria-hidden className="pointer-events-none absolute -bottom-20 left-1/4 h-64 w-64 rounded-full bg-black/10 blur-3xl" />
              <Icon name="music" className="pointer-events-none absolute right-8 top-6 hidden h-9 w-9 -rotate-12 text-white/20 sm:block sm:right-12 sm:top-8 sm:h-11 sm:w-11" />
              <Icon name="sparkles" className="pointer-events-none absolute bottom-8 left-10 hidden h-7 w-7 rotate-12 text-white/20 sm:block" />

              <div className="relative flex flex-wrap items-center justify-between gap-3">
                <BrandMark compact />
                <span className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-[.7rem] font-bold uppercase tracking-[.14em] text-white backdrop-blur-sm">
                  {current.groupName ? `${current.groupName} · ` : ""}{current.position + 1}. sıra
                </span>
              </div>

              <div className="relative mt-6 flex flex-col items-center gap-6 text-white sm:flex-row sm:gap-10">
                <div className="relative shrink-0">
                  <div
                    aria-hidden
                    className="absolute -inset-3 -rotate-6 bg-white/15"
                    style={{ borderRadius: "42% 58% 65% 35% / 45% 40% 60% 55%" }}
                  />
                  <span
                    className="relative grid h-32 w-32 place-items-center overflow-hidden bg-white/20 text-3xl font-bold shadow-[0_16px_40px_rgba(0,0,0,.25)] sm:h-44 sm:w-44"
                    style={{ borderRadius: "42% 58% 65% 35% / 45% 40% 60% 55%" }}
                  >
                    {current.hasPhoto && current.studentId
                      /* eslint-disable-next-line @next/next/no-img-element */
                      ? <img src={studentPhotoUrl(current.studentId, current.photoVersion)} alt={current.studentName ?? ""} className="h-full w-full object-cover" />
                      : (current.studentName ?? "?").split(" ").map((part) => part[0]).slice(0, 2).join("")}
                  </span>
                </div>

                <div className="min-w-0 flex-1 text-center sm:text-left">
                  {current.instrumentName && (() => {
                    const badge = instrumentBadgeStyle(current.instrumentName);
                    return (
                      <span className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-3 py-1 text-sm font-bold backdrop-blur-sm">
                        <Icon name={badge.icon} className="h-4 w-4" />{current.instrumentName}
                      </span>
                    );
                  })()}

                  <p className="mt-3 truncate font-serif text-2xl font-bold italic text-white/90 sm:text-3xl">{current.studentName}</p>

                  {/* "Büyük punto" tam olarak burası: salondan da okunabilecek eser adı. */}
                  <p className="mt-2 font-serif text-4xl font-bold leading-[1.1] sm:text-6xl lg:text-7xl">{current.pieceTitle}</p>
                  {current.composer && <p className="mt-2 text-xl text-white/70 sm:text-2xl">{current.composer}</p>}

                  <p className="mt-4 flex flex-wrap items-center justify-center gap-x-3 gap-y-1 text-sm text-white/60 sm:justify-start">
                    {current.teacherName && <span>{current.teacherName}</span>}
                    {current.studentPieces.length > 1 && (
                      <span className="rounded-full bg-white/15 px-2 py-0.5 font-bold text-white/85">
                        {current.studentPieces.length} eserden {current.studentPieceIndex + 1}.si
                      </span>
                    )}
                  </p>
                </div>
              </div>
            </div>
          )}
        </div>

        {/* SIRADAKİ / ONDAN SONRAKİ — kulisin çalışma alanı. */}
        <div className="mt-6 flex flex-col gap-3 sm:flex-row">
          <UpNextCard label="Sıradaki" item={stage.next} />
          <UpNextCard label="Ondan sonraki" item={stage.onDeck} />
        </div>
      </section>

      {isAdmin && (
        <section className="app-card p-3">
          <div className="flex flex-wrap items-center gap-2">
            {stage.status !== "Live" && (
              <button type="button" onClick={() => send(stage.status === "Completed" ? "reopen" : "start")} disabled={control.isPending || stage.totalItems === 0} className="btn btn-primary">
                {stage.status === "Completed" ? "Yeniden aç" : "Gösteriyi başlat"}
              </button>
            )}
            {isLive && (
              <>
                <button type="button" onClick={() => send("back")} disabled={control.isPending} className="btn btn-quiet"><Icon name="arrow-left" className="h-4 w-4" />Geri</button>
                <button type="button" onClick={() => send("advance")} disabled={control.isPending} className="btn btn-primary flex-1 sm:flex-none">
                  Sıradakine geç<Icon name="arrow-right" className="h-4 w-4" />
                </button>
                <button type="button" onClick={() => send("finish")} disabled={control.isPending} className="btn btn-quiet">Gösteriyi bitir</button>
                <p className="text-meta w-full sm:ml-auto sm:w-auto">Boşluk veya → ileri, ← geri</p>
              </>
            )}
          </div>
          {error && <p role="alert" className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
        </section>
      )}
    </div>
  );
}
