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

// Şablon PNG'leri /public/bg altında, 1536x1024 (3:2) - Canva/AI ile üretilmiş, kesikli
// çemberli bir "ÖĞRENCİ FOTOĞRAFI BURAYA GELECEK" alanı ve iki noktalı satır + "ENSTRÜMAN"
// alt çizgisi taşıyorlar. Koordinatlar görselin kendi genişlik/yüksekliğine göre yüzde -
// dıştaki kutu aspect-[3/2] ile görselle birebir aynı orana kilitlendiği için bu yüzdeler
// her ekran boyutunda hizalı kalır (bkz. sohbet - piksel piksel kalibre edildi).
interface PerformerTemplate {
  src: string;
  photo: { x: number; y: number; w: number; h: number };
  name: { x: number; y: number; w: number };
  instrument: { x: number; y: number; w: number };
}

// Her enstrüman için iki varyant - art arda aynı enstrümandan gelen öğrenciler aynı
// afişi görmesin diye `position`e göre aralarında dönülür (bkz. pickTemplate).
const PERFORMER_TEMPLATES: Record<string, PerformerTemplate[]> = {
  Piyano: [
    { src: "/bg/piano_bg.png", photo: { x: 16, y: 32, w: 32, h: 42 }, name: { x: 52, y: 38.5, w: 24 }, instrument: { x: 52, y: 55, w: 24 } },
    { src: "/bg/piano2_bg.png", photo: { x: 12, y: 32, w: 29, h: 42 }, name: { x: 46, y: 41.5, w: 25 }, instrument: { x: 46, y: 59.5, w: 25 } },
  ],
  Keman: [
    { src: "/bg/keman_bg.png", photo: { x: 13, y: 32, w: 28, h: 42 }, name: { x: 45, y: 40, w: 25 }, instrument: { x: 45, y: 58.5, w: 25 } },
    { src: "/bg/keman2_bg.png", photo: { x: 13, y: 35, w: 29, h: 39 }, name: { x: 45, y: 41.5, w: 25 }, instrument: { x: 45, y: 60, w: 25 } },
  ],
  Bateri: [
    { src: "/bg/bateri_bg.png", photo: { x: 13, y: 32, w: 28, h: 42 }, name: { x: 47, y: 40, w: 25 }, instrument: { x: 47, y: 58, w: 25 } },
    { src: "/bg/bateri2_bg.png", photo: { x: 14, y: 32, w: 28, h: 42 }, name: { x: 47, y: 39, w: 25 }, instrument: { x: 47, y: 60, w: 25 } },
  ],
};

function pickTemplate(instrumentName: string | null, position: number): PerformerTemplate | null {
  const variants = instrumentName ? PERFORMER_TEMPLATES[instrumentName] : undefined;
  return variants?.[position % variants.length] ?? null;
}

// Kullanıcı isteği: "background resmin üzerine progress barı ekleyebilirsin" - hem şablon
// görselinin hem de şablonsuz enstrümanların (gitar/çello/resim) gradyan kartının altına
// aynı şerit biniyor, ikisi arasında geçişte tutarlı görünsün diye.
function PerformerProgressStrip({ position, totalItems, groupName }: { position: number; totalItems: number; groupName: string | null }) {
  const percent = totalItems > 0 ? Math.min(100, Math.round(((position + 1) / totalItems) * 100)) : 0;
  return (
    <div className="absolute inset-x-0 bottom-0 bg-black/35 px-[4cqw] py-[1.6cqh] backdrop-blur-[2px]">
      <p className="truncate font-bold text-white" style={{ fontSize: "2.2cqw" }}>
        {groupName ? `${groupName} · ` : ""}{position + 1} / {totalItems} sıra
      </p>
      <div className="mt-[.8cqh] h-[1.6cqh] w-full overflow-hidden rounded-full bg-white/25">
        <div className="h-full rounded-full bg-white transition-[width] duration-500" style={{ width: `${percent}%` }} />
      </div>
    </div>
  );
}

function TemplatedPerformerCard({ current, totalItems, template, fillHeight }: { current: StageItem; totalItems: number; template: PerformerTemplate; fillHeight?: boolean }) {
  const initials = (current.studentName ?? "?").split(" ").map((part) => part[0]).slice(0, 2).join("");
  return (
    <div
      className={`relative aspect-[3/2] overflow-hidden rounded-[2rem] shadow-[0_20px_50px_rgba(0,0,0,.35)] [container-type:size] ${
        fillHeight ? "mx-auto h-full max-w-full" : "w-full"
      }`}
    >
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={template.src} alt="" className="absolute inset-0 h-full w-full object-cover" />

      <div
        className="absolute overflow-hidden rounded-full bg-[var(--brand-soft)]"
        style={{ left: `${template.photo.x}%`, top: `${template.photo.y}%`, width: `${template.photo.w}%`, height: `${template.photo.h}%` }}
      >
        {current.hasPhoto && current.studentId
          /* eslint-disable-next-line @next/next/no-img-element */
          ? <img src={studentPhotoUrl(current.studentId, current.photoVersion)} alt={current.studentName ?? ""} className="h-full w-full object-cover" />
          : <span className="grid h-full w-full place-items-center font-bold text-[var(--brand-strong)]" style={{ fontSize: "11cqw" }}>{initials}</span>}
      </div>

      <p
        className="absolute truncate text-center font-serif font-bold text-[#2c2420]"
        style={{ left: `${template.name.x}%`, top: `${template.name.y}%`, width: `${template.name.w}%`, fontSize: "3.4cqw" }}
      >
        {current.studentName}
      </p>

      {current.instrumentName && (
        <p
          className="absolute truncate text-center font-bold text-[#2c2420]"
          style={{ left: `${template.instrument.x}%`, top: `${template.instrument.y}%`, width: `${template.instrument.w}%`, fontSize: "2.4cqw" }}
        >
          {current.instrumentName}
        </p>
      )}

      <PerformerProgressStrip position={current.position} totalItems={totalItems} groupName={current.groupName} />
    </div>
  );
}

// Şablon PNG'si olmayan enstrümanlar (gitar/çello/resim) için - önceki tasarımın gradyan/blob
// kartı, artık yalnızca kimlik bilgisini taşıyor (eser adı paylaşılan bloğa taşındı, bkz. aşağı).
function GradientPerformerCard({ current, totalItems, fillHeight }: { current: StageItem; totalItems: number; fillHeight?: boolean }) {
  return (
    <div
      className={`relative aspect-[3/2] overflow-hidden rounded-[2rem] bg-[linear-gradient(160deg,var(--sidebar-from)_0%,#c15a4a_45%,var(--sidebar-to)_100%)] [container-type:size] ${
        fillHeight ? "mx-auto h-full max-w-full" : "w-full"
      }`}
    >
      <div aria-hidden className="pointer-events-none absolute -left-16 -top-16 h-56 w-56 rounded-full bg-white/10 blur-2xl" />
      <div aria-hidden className="pointer-events-none absolute -right-10 top-1/3 h-40 w-40 rounded-full bg-white/15 blur-2xl" />
      <div aria-hidden className="pointer-events-none absolute -bottom-20 left-1/4 h-64 w-64 rounded-full bg-black/10 blur-3xl" />
      <Icon name="music" className="pointer-events-none absolute right-8 top-6 hidden h-9 w-9 -rotate-12 text-white/20 sm:block sm:right-12 sm:top-8 sm:h-11 sm:w-11" />

      {/* Şablon görsellerinin aksine burada sabit bir "üstten %X" varsayımı yok - dikey
          alan içerik akışıyla paylaşılıyor ki dar/mobil genişlikte (fotoğraf+metin alt alta
          dizildiğinde) içerik alttaki ilerleme şeridinin üzerine binmesin (gerçek bir dizilim
          hatası olarak bulundu - bkz. sohbet). `pb` alttaki şeridin yüksekliğini önden ayırır. */}
      <div className="relative flex h-full flex-col p-[4cqw] pb-[22cqh]">
        <BrandMark compact />
        <div className="flex flex-1 flex-col items-center justify-center gap-[3cqw] text-white sm:flex-row sm:gap-[4cqw]">
          <div className="relative shrink-0">
            <div aria-hidden className="absolute -inset-[6%] -rotate-6 bg-white/15" style={{ borderRadius: "42% 58% 65% 35% / 45% 40% 60% 55%" }} />
            <span
              className="relative grid place-items-center overflow-hidden bg-white/20 font-bold shadow-[0_16px_40px_rgba(0,0,0,.25)]"
              style={{ borderRadius: "42% 58% 65% 35% / 45% 40% 60% 55%", width: "22cqw", height: "22cqw", fontSize: "5cqw" }}
            >
              {current.hasPhoto && current.studentId
                /* eslint-disable-next-line @next/next/no-img-element */
                ? <img src={studentPhotoUrl(current.studentId, current.photoVersion)} alt={current.studentName ?? ""} className="h-full w-full object-cover" />
                : (current.studentName ?? "?").split(" ").map((part) => part[0]).slice(0, 2).join("")}
            </span>
          </div>
          <div className="min-w-0 text-center sm:text-left">
            {current.instrumentName && (() => {
              const badge = instrumentBadgeStyle(current.instrumentName);
              return (
                <span className="inline-flex items-center gap-1.5 rounded-full bg-white/15 px-[3cqw] py-[1cqw] font-bold backdrop-blur-sm" style={{ fontSize: "2.2cqw" }}>
                  <Icon name={badge.icon} style={{ width: "2.6cqw", height: "2.6cqw" }} />{current.instrumentName}
                </span>
              );
            })()}
            <p className="mt-[2cqw] truncate font-serif font-bold italic text-white/90" style={{ fontSize: "3.2cqw" }}>{current.studentName}</p>
          </div>
        </div>
      </div>

      <PerformerProgressStrip position={current.position} totalItems={totalItems} groupName={current.groupName} />
    </div>
  );
}

export default function StagePage() {
  const params = useParams<{ showId: string }>();
  const showId = params.showId;
  const { data: me } = useMe();
  const { data: stage, isLoading } = useStage(showId);
  const control = useStageControl(showId);
  const [error, setError] = useState<string | null>(null);
  const [isFullscreen, setIsFullscreen] = useState(false);
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

  // Tam ekranda ekran boyu sabit (projeksiyon/TV) - şablon kartı ve alt metinler artık
  // genişliğe göre değil, bu durumda paylaşılan yüksekliğe göre ölçekleniyor (bkz.
  // aşağıdaki fillHeight kullanımı). Normal sayfa akışında (tam ekran değilken) sayfa zaten
  // kayabildiği için eski genişlik odaklı boyutlandırma korunuyor.
  useEffect(() => {
    function onFullscreenChange() {
      setIsFullscreen(document.fullscreenElement?.id === "stage-screen");
    }
    document.addEventListener("fullscreenchange", onFullscreenChange);
    return () => document.removeEventListener("fullscreenchange", onFullscreenChange);
  }, []);

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

      <section
        id="stage-screen"
        className={`overflow-hidden rounded-3xl bg-[#1a1512] p-5 text-white sm:p-8 ${
          isFullscreen ? "flex h-screen flex-col" : ""
        }`}
      >
        <header className="flex shrink-0 flex-wrap items-baseline justify-between gap-2">
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

        <div className="mt-3 shrink-0"><ProgressBar value={stage.completedItems} total={stage.totalItems} /></div>

        {/* ŞU AN — ekranın asıl işi. Tam ekranda bu blok kalan yüksekliği paylaşan bir
            flex kutusu (min-h-0 olmadan flex-1 büyüyen çocuk kendi içeriğine göre taşar -
            bkz. sohbet, şablon kartı ekranın tamamını genişliğe göre kaplayıp geri kalan
            her şeyi ekran dışına itiyordu). */}
        <div className={`mt-6 sm:mt-8 ${isFullscreen ? "flex min-h-0 flex-1 flex-col" : "min-h-[16rem]"}`}>
          {stage.status === "Draft" && (
            <div className={`grid place-items-center text-center ${isFullscreen ? "h-full" : "min-h-[16rem]"}`}>
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
            <div className={`grid place-items-center text-center ${isFullscreen ? "h-full" : "min-h-[16rem]"}`}>
              <div>
                <p className="font-serif text-3xl text-white">Gösteri tamamlandı</p>
                <p className="mt-2 text-sm text-white/40">{stage.totalItems} sıra sahnelendi.</p>
              </div>
            </div>
          )}

          {isLive && !current && (
            <div className={`grid place-items-center text-center ${isFullscreen ? "h-full" : "min-h-[16rem]"}`}>
              <p className="font-serif text-2xl text-white/60">Sahne boş</p>
            </div>
          )}

          {isLive && current && current.kind !== "Performance" && (
            <div className={`grid place-items-center text-center ${isFullscreen ? "h-full" : "min-h-[16rem]"}`}>
              <div>
                <p className="text-[.7rem] font-bold uppercase tracking-[.2em] text-white/40">{SHOW_ITEM_KIND_LABEL[current.kind]}</p>
                <p className="mt-3 font-serif text-5xl font-bold leading-tight sm:text-7xl">{current.pieceTitle ?? SHOW_ITEM_KIND_LABEL[current.kind]}</p>
                {current.note && <p className="mt-3 text-lg text-white/50">{current.note}</p>}
              </div>
            </div>
          )}

          {isLive && current && current.kind === "Performance" && (() => {
            const template = pickTemplate(current.instrumentName, current.position);
            return (
              <div className={isFullscreen ? "flex min-h-0 flex-1 flex-col items-center" : ""}>
                <div className={isFullscreen ? "min-h-0 flex-1" : ""}>
                  {template
                    ? <TemplatedPerformerCard current={current} totalItems={stage.totalItems} template={template} fillHeight={isFullscreen} />
                    : <GradientPerformerCard current={current} totalItems={stage.totalItems} fillHeight={isFullscreen} />}
                </div>

                {/* "Büyük punto" tam olarak burası: kullanıcı isteği - salondan da okunabilecek
                    eser adı. Şablonların hiçbirinde eser adı için ayrılmış bir alan yok
                    (yalnızca öğrenci/enstrüman), o yüzden kartın hemen altında, ortak. Tam
                    ekranda genişliğe göre değil (sm:/lg:) sabit, ekran yüksekliğiyle uyumlu
                    puntolar kullanılıyor - aksi halde geniş bir projeksiyonda lg:text-7xl
                    tek başına kalan yüksekliği taşırıyordu. */}
                <div className={`shrink-0 text-center ${isFullscreen ? "mt-3" : "mt-6"}`}>
                  <p className={`font-serif font-bold leading-[1.1] ${isFullscreen ? "text-3xl" : "text-4xl sm:text-6xl lg:text-7xl"}`}>
                    {current.pieceTitle}
                  </p>
                  {current.composer && (
                    <p className={`text-white/70 ${isFullscreen ? "mt-1 text-base" : "mt-2 text-xl sm:text-2xl"}`}>{current.composer}</p>
                  )}

                  <p className={`flex flex-wrap items-center justify-center gap-x-3 gap-y-1 text-white/60 ${isFullscreen ? "mt-2 text-xs" : "mt-4 text-sm"}`}>
                    {current.teacherName && <span>{current.teacherName}</span>}
                    {current.studentPieces.length > 1 && (
                      <span className="rounded-full bg-white/15 px-2 py-0.5 font-bold text-white/85">
                        {current.studentPieces.length} eserden {current.studentPieceIndex + 1}.si
                      </span>
                    )}
                  </p>
                </div>
              </div>
            );
          })()}
        </div>

        {/* SIRADAKİ / ONDAN SONRAKİ — kulisin çalışma alanı. */}
        <div className={`mt-6 flex flex-col gap-3 sm:flex-row ${isFullscreen ? "shrink-0" : ""}`}>
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
