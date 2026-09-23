"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useCallback, useEffect, useState } from "react";
import { BrandMark, Icon, instrumentBadgeStyle } from "@/components/icons";
import { useMe } from "@/lib/use-auth";
import styles from "./stage.module.css";
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
    <div className="h-1.5 w-full overflow-hidden rounded-full bg-[#c7755a]/20" role="progressbar" aria-valuenow={value} aria-valuemin={0} aria-valuemax={total}>
      <div className="h-full rounded-full bg-[linear-gradient(90deg,#dc694f,#f49a58)] transition-[width] duration-500" style={{ width: `${percent}%` }} />
    </div>
  );
}

function UpNextCard({ label, item }: { label: string; item: StageItem | null }) {
  return (
    <article className="min-w-0 flex-1 rounded-2xl border border-[#e9cfc0] bg-white/65 p-4 shadow-[0_10px_30px_rgba(126,65,45,.08)] backdrop-blur-md">
      <p className="text-[.75rem] font-bold uppercase tracking-[.18em] text-[#9d5a4d]">{label}</p>
      {!item && <p className="mt-2 text-sm text-[#9a7c6f]">—</p>}
      {item && (
        <div className="mt-2 flex items-center gap-3">
          <span className="grid h-12 w-12 shrink-0 place-items-center overflow-hidden rounded-xl bg-[#fae5d8] text-sm font-bold text-[#9d4f43]">
            {item.hasPhoto && item.studentId
              /* eslint-disable-next-line @next/next/no-img-element */
              ? <img src={studentPhotoUrl(item.studentId, item.photoVersion)} alt="" className="h-full w-full object-cover" />
              : item.kind === "Performance"
                ? (item.studentName ?? "?").split(" ").map((part) => part[0]).slice(0, 2).join("")
                : <Icon name={item.kind === "Intermission" ? "clock" : "bell"} className="h-4 w-4" />}
          </span>
          <div className="min-w-0">
            <p className="truncate text-base font-bold text-[#3e2d28]">
              {item.kind === "Performance" ? item.studentName : (item.pieceTitle ?? SHOW_ITEM_KIND_LABEL[item.kind])}
            </p>
            <p className="truncate text-sm text-[#7f675e]">
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
  details: { x: number; y: number; w: number };
}

// /public/bg içindeki bütün yıl sonu gösterisi şablonları tek eşleme üzerinden seçilir.
// Piyano için iki varyant olduğu için art arda gelen öğrencilerde `position` ile dönüşür.
const PERFORMER_TEMPLATES: Record<string, PerformerTemplate[]> = {
  piyano: [
    { src: "/bg/piano_bg.png", photo: { x: 13.5, y: 32, w: 27.5, h: 41.5 }, details: { x: 45, y: 39, w: 27 } },
    { src: "/bg/piano2_bg.png", photo: { x: 17, y: 32.5, w: 30.5, h: 41.5 }, details: { x: 48, y: 39, w: 28 } },
  ],
  keman: [
    { src: "/bg/keman_bg.png", photo: { x: 7.5, y: 32.5, w: 31.5, h: 41.5 }, details: { x: 43, y: 39, w: 29 } },
  ],
  bateri: [
    { src: "/bg/bateri_bg.png", photo: { x: 15.2, y: 32.5, w: 27.5, h: 41.5 }, details: { x: 47, y: 39, w: 28 } },
  ],
  gitar: [
    { src: "/bg/gitar_bg.png", photo: { x: 17.1, y: 32.5, w: 30.5, h: 41.5 }, details: { x: 49, y: 39, w: 27 } },
  ],
  "çello": [
    { src: "/bg/cello_bg.png", photo: { x: 17.1, y: 32.5, w: 30.5, h: 41.5 }, details: { x: 49, y: 39, w: 27 } },
  ],
  cello: [
    { src: "/bg/cello_bg.png", photo: { x: 17.1, y: 32.5, w: 30.5, h: 41.5 }, details: { x: 49, y: 39, w: 27 } },
  ],
  viyolonsel: [
    { src: "/bg/cello_bg.png", photo: { x: 17.1, y: 32.5, w: 30.5, h: 41.5 }, details: { x: 49, y: 39, w: 27 } },
  ],
  resim: [
    { src: "/bg/resim_bg.png", photo: { x: 17.1, y: 32.5, w: 30.5, h: 41.5 }, details: { x: 49, y: 39, w: 27 } },
  ],
};

function pickTemplate(instrumentName: string | null, position: number): PerformerTemplate | null {
  const key = instrumentName?.trim().toLocaleLowerCase("tr-TR");
  const variants = key ? PERFORMER_TEMPLATES[key] : undefined;
  return variants?.[position % variants.length] ?? null;
}

// Kullanıcı isteği: "background resmin üzerine progress barı ekleyebilirsin" - hem şablon
// görselinin hem de şablonsuz enstrümanların (gitar/çello/resim) gradyan kartının altına
// aynı şerit biniyor, ikisi arasında geçişte tutarlı görünsün diye.
function PerformerProgressStrip({ position, totalItems, groupName }: { position: number; totalItems: number; groupName: string | null }) {
  const percent = totalItems > 0 ? Math.min(100, Math.round(((position + 1) / totalItems) * 100)) : 0;
  return (
    <div className="absolute inset-x-0 bottom-0 bg-[linear-gradient(180deg,rgba(255,250,246,0),rgba(255,250,246,.95)_42%)] px-[4cqw] pb-[1.4cqh] pt-[4.2cqh] backdrop-blur-[1px]">
      <p className="truncate font-bold text-[#76483e]" style={{ fontSize: "clamp(.75rem, 1.75cqw, 2.5rem)" }}>
        {groupName ? `${groupName} · ` : ""}{position + 1} / {totalItems} sıra
      </p>
      <div className="mt-[.7cqh] h-[1.2cqh] w-full overflow-hidden rounded-full bg-[#c47560]/20">
        <div className="h-full rounded-full bg-[linear-gradient(90deg,#d7564b,#f19a58)] transition-[width] duration-500" style={{ width: `${percent}%` }} />
      </div>
    </div>
  );
}

function TemplatedPerformerCard({ current, totalItems, template, fillHeight }: { current: StageItem; totalItems: number; template: PerformerTemplate; fillHeight?: boolean }) {
  const initials = (current.studentName ?? "?").split(" ").map((part) => part[0]).slice(0, 2).join("");
  return (
    <div
      className={`relative aspect-[3/2] overflow-hidden rounded-[2rem] border border-white/80 shadow-[0_24px_70px_rgba(124,65,48,.2)] [container-type:size] ${styles.slideReveal} ${
        fillHeight ? "mx-auto h-full max-w-full" : "w-full"
      }`}
    >
      {/* eslint-disable-next-line @next/next/no-img-element */}
      <img src={template.src} alt="" className={`absolute inset-0 h-full w-full object-cover ${styles.slideImage}`} />

      <div
        className="absolute overflow-hidden rounded-full bg-[var(--brand-soft)]"
        style={{ left: `${template.photo.x}%`, top: `${template.photo.y}%`, width: `${template.photo.w}%`, height: `${template.photo.h}%` }}
      >
        {current.hasPhoto && current.studentId
          /* eslint-disable-next-line @next/next/no-img-element */
          ? <img src={studentPhotoUrl(current.studentId, current.photoVersion)} alt={current.studentName ?? ""} className="h-full w-full object-cover" />
          : <span className="grid h-full w-full place-items-center font-bold text-[var(--brand-strong)]" style={{ fontSize: "11cqw" }}>{initials}</span>}
      </div>

      <div
        className={`absolute text-center ${styles.performerDetails}`}
        style={{ left: `${template.details.x}%`, top: `${template.details.y}%`, width: `${template.details.w}%` }}
      >
        <p className="truncate font-serif font-bold text-[#6f302e]" style={{ fontSize: "clamp(1rem, 2.9cqw, 4.5rem)", lineHeight: 1.05 }}>{current.studentName}</p>
        <p className={`mt-[1cqh] font-serif font-bold text-[#2d2e31] ${styles.pieceTitle}`} style={{ fontSize: "clamp(.875rem, 1.95cqw, 3rem)", lineHeight: 1.12 }}>{current.pieceTitle}</p>
        {current.composer && <p className="mt-[.55cqh] truncate font-semibold text-[#775d55]" style={{ fontSize: "clamp(.75rem, 1.25cqw, 2rem)" }}>{current.composer}</p>}
      </div>

      <PerformerProgressStrip position={current.position} totalItems={totalItems} groupName={current.groupName} />
    </div>
  );
}

// Şablon PNG'si olmayan enstrümanlar (gitar/çello/resim) için - önceki tasarımın gradyan/blob
// kartı, artık yalnızca kimlik bilgisini taşıyor (eser adı paylaşılan bloğa taşındı, bkz. aşağı).
function GradientPerformerCard({ current, totalItems, fillHeight }: { current: StageItem; totalItems: number; fillHeight?: boolean }) {
  return (
    <div
      className={`relative aspect-[3/2] overflow-hidden rounded-[2rem] border border-white/80 bg-[linear-gradient(145deg,#fffaf3_0%,#fde2cf_52%,#ef9f8e_100%)] shadow-[0_24px_70px_rgba(124,65,48,.2)] [container-type:size] ${styles.slideReveal} ${
        fillHeight ? "mx-auto h-full max-w-full" : "w-full"
      }`}
    >
      <div aria-hidden className="pointer-events-none absolute -left-16 -top-16 h-56 w-56 rounded-full bg-[#f29070]/20 blur-2xl" />
      <div aria-hidden className="pointer-events-none absolute -right-10 top-1/3 h-40 w-40 rounded-full bg-white/45 blur-2xl" />
      <div aria-hidden className="pointer-events-none absolute -bottom-20 left-1/4 h-64 w-64 rounded-full bg-[#b74f5d]/10 blur-3xl" />
      <Icon name="music" className="pointer-events-none absolute right-8 top-6 hidden -rotate-12 text-[#b74f5d]/30 sm:block sm:right-12 sm:top-8 sm:h-11 sm:w-11" />

      {/* Şablon görsellerinin aksine burada sabit bir "üstten %X" varsayımı yok - dikey
          alan içerik akışıyla paylaşılıyor ki dar/mobil genişlikte (fotoğraf+metin alt alta
          dizildiğinde) içerik alttaki ilerleme şeridinin üzerine binmesin (gerçek bir dizilim
          hatası olarak bulundu - bkz. sohbet). `pb` alttaki şeridin yüksekliğini önden ayırır. */}
      <div className="relative flex h-full flex-col p-[4cqw] pb-[22cqh]">
        <BrandMark compact />
        <div className="flex flex-1 flex-col items-center justify-center gap-[3cqw] text-[#3e2d28] sm:flex-row sm:gap-[4cqw]">
          <div className="relative shrink-0">
            <div aria-hidden className="absolute -inset-[6%] -rotate-6 bg-[#d45f52]/20" style={{ borderRadius: "42% 58% 65% 35% / 45% 40% 60% 55%" }} />
            <span
              className="relative grid place-items-center overflow-hidden bg-white/75 font-bold text-[#a84943] shadow-[0_16px_40px_rgba(124,65,48,.18)]"
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
                <span className="inline-flex items-center gap-1.5 rounded-full bg-white/65 px-[3cqw] py-[1cqw] font-bold backdrop-blur-sm" style={{ fontSize: "clamp(.75rem, 2.2cqw, 3rem)" }}>
                  <Icon name={badge.icon} style={{ width: "clamp(.875rem, 2.6cqw, 3.5rem)", height: "clamp(.875rem, 2.6cqw, 3.5rem)" }} />{current.instrumentName}
                </span>
              );
            })()}
            <p className="mt-[2cqw] truncate font-serif font-bold italic text-[#6f302e]" style={{ fontSize: "clamp(1rem, 3.2cqw, 5rem)" }}>{current.studentName}</p>
            <p className={`mt-[1.2cqw] font-serif font-bold text-[#2d2e31] ${styles.pieceTitle}`} style={{ fontSize: "clamp(.875rem, 2.7cqw, 4rem)", lineHeight: 1.12 }}>{current.pieceTitle}</p>
            {current.composer && <p className="mt-[.8cqw] truncate font-semibold text-[#775d55]" style={{ fontSize: "clamp(.75rem, 1.6cqw, 2.5rem)" }}>{current.composer}</p>}
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
  const { data: stage, isLoading, isError } = useStage(showId);
  const control = useStageControl(showId);
  const [error, setError] = useState<string | null>(null);
  const [isFullscreen, setIsFullscreen] = useState(false);
  // iPhone Safari öğe tam ekranını desteklemiyor; düğmeyi yalnızca çalışacağı yerde göster.
  // SSR ile uyuşmazlık olmasın diye mount sonrası okunur.
  const [canFullscreen, setCanFullscreen] = useState(false);
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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- tarayıcı yeteneği yalnızca istemcide okunabilir
    setCanFullscreen(document.fullscreenEnabled === true);
    function onFullscreenChange() {
      setIsFullscreen(document.fullscreenElement?.id === "stage-screen");
    }
    document.addEventListener("fullscreenchange", onFullscreenChange);
    return () => document.removeEventListener("fullscreenchange", onFullscreenChange);
  }, []);

  if (isLoading) return <div className="skeleton h-[70vh] rounded-3xl" />;
  if (!stage) {
    return (
      <p role={isError ? "alert" : undefined} className="app-card p-6 text-sm text-[var(--muted)]">
        {isError ? "Sahne bilgisi yüklenemedi. Bağlantınızı kontrol edip sayfayı yenileyin." : "Gösteri bulunamadı."}
      </p>
    );
  }

  const current = stage.current;
  const behindMinutes = stage.elapsedMinutes - stage.totalDurationMinutes;

  // Aynı düğmeler hem sayfa akışında hem tam ekranın alt çubuğunda kullanılır.
  const controls = (
    <>
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
        </>
      )}
    </>
  );

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
          {canFullscreen && (
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
          )}
        </div>
      </div>

      <section
        id="stage-screen"
        className={`overflow-hidden rounded-3xl p-5 sm:p-8 ${styles.screen} ${
          isFullscreen ? "flex h-screen w-screen flex-col rounded-none" : ""
        }`}
      >
        <div key={current?.id ?? stage.status} aria-hidden className={styles.motionLayer}>
          <Icon name="music" className={`${styles.floatingNote} ${styles.noteOne}`} />
          <Icon name="music" className={`${styles.floatingNote} ${styles.noteTwo}`} />
          <Icon name="music" className={`${styles.floatingNote} ${styles.noteThree}`} />
        </div>

        <header className="flex shrink-0 flex-wrap items-baseline justify-between gap-2">
          <h1 className="font-serif text-lg font-bold text-[#623b33]">{stage.title}</h1>
          <p className="text-sm tabular-nums text-[#8c6c60]">
            {stage.completedItems}/{stage.totalItems} sıra
            {stage.startedAt && <> · {stage.elapsedMinutes} dk geçti</>}
            {stage.startedAt && stage.totalDurationMinutes > 0 && (
              <> · <span className={behindMinutes > 5 ? "text-[var(--danger-strong)]" : "text-[#8c6c60]"}>
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
                <p className="font-serif text-2xl text-[#6f443a]">Gösteri henüz başlamadı</p>
                <p className="mt-2 text-sm text-[#8c6c60]">
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
                <p className="font-serif text-3xl text-[#6f302e]">Gösteri tamamlandı</p>
                <p className="mt-2 text-sm text-[#8c6c60]">{stage.totalItems} sıra sahnelendi.</p>
              </div>
            </div>
          )}

          {isLive && !current && (
            <div className={`grid place-items-center text-center ${isFullscreen ? "h-full" : "min-h-[16rem]"}`}>
              <p className="font-serif text-2xl text-[#8c6c60]">Sahne boş</p>
            </div>
          )}

          {isLive && current && current.kind !== "Performance" && (
            <div className={`grid place-items-center text-center ${isFullscreen ? "h-full" : "min-h-[16rem]"}`}>
              <div>
                <p className="text-[.75rem] font-bold uppercase tracking-[.2em] text-[#9d5a4d]">{SHOW_ITEM_KIND_LABEL[current.kind]}</p>
                <p className="mt-3 font-serif text-5xl font-bold leading-tight text-[#623b33] sm:text-7xl">{current.pieceTitle ?? SHOW_ITEM_KIND_LABEL[current.kind]}</p>
                {current.note && <p className="mt-3 text-lg text-[#8c6c60]">{current.note}</p>}
              </div>
            </div>
          )}

          {isLive && current && current.kind === "Performance" && (() => {
            const template = pickTemplate(current.instrumentName, current.position);
            return (
              <div className={isFullscreen ? "flex min-h-0 flex-1 flex-col items-center" : ""}>
                <div className={isFullscreen ? "min-h-0 flex-1" : ""}>
                  {template
                    ? <TemplatedPerformerCard key={current.id} current={current} totalItems={stage.totalItems} template={template} fillHeight={isFullscreen} />
                    : <GradientPerformerCard key={current.id} current={current} totalItems={stage.totalItems} fillHeight={isFullscreen} />}
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

        {/* Tam ekran yalnızca bu bölümü kapsar; dokunmatik tablette klavye olmadığı için
            ilerletme düğmeleri tam ekranın içinde de erişilebilir olmalı. */}
        {isAdmin && isFullscreen && (
          <div className="mt-3 flex shrink-0 flex-wrap items-center gap-2 rounded-2xl bg-white/70 p-2 backdrop-blur-md">
            {controls}
            <button type="button" onClick={() => void document.exitFullscreen()} className="btn btn-quiet sm:ml-auto">Tam ekrandan çık</button>
            {error && <p role="alert" className="w-full text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
          </div>
        )}
      </section>

      {isAdmin && !isFullscreen && (
        <section className="app-card p-3">
          <div className="flex flex-wrap items-center gap-2">
            {controls}
            {isLive && <p className="text-meta w-full sm:ml-auto sm:w-auto">Boşluk veya → ileri, ← geri</p>}
          </div>
          {error && <p role="alert" className="mt-2 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
        </section>
      )}
    </div>
  );
}
