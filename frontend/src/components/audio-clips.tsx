"use client";

import { useEffect, useRef, useState } from "react";
import { Icon } from "@/components/icons";
import { ApiError } from "@/lib/api";
import {
  AUDIO_MAX_BYTES,
  AUDIO_MAX_SECONDS,
  formatBytes,
  pickRecorderFormat,
  staffClipUrl,
  useDeleteAudioClip,
  useSetAudioConsent,
  useShareAudioClip,
  useStarAudioClip,
  useStudentAudioClips,
  useUploadAudioClip,
  type AudioClip,
} from "@/lib/audio-clips";

// Ses Defteri (issue #52). Kayıt tarayıcıda sıkıştırılır (mono Opus 32 kbps), sunucu dönüştürme
// yapmaz. Ses işleme (yankı/gürültü bastırma, otomatik kazanç) kapalı: konuşma için yapılmış bu
// filtreler enstrüman sesini bozuyor, özellikle piyanonun sönümünü kesiyor.
type Phase = "idle" | "recording" | "recorded" | "saved";

export function LessonAudioRecorder({ lessonId, studentId, studentName }: { lessonId: string; studentId: string; studentName: string }) {
  const { data, isLoading } = useStudentAudioClips(studentId);
  const upload = useUploadAudioClip(studentId);
  const [phase, setPhase] = useState<Phase>("idle");
  const [elapsed, setElapsed] = useState(0);
  const [blob, setBlob] = useState<Blob | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const [pieceTitle, setPieceTitle] = useState("");
  const [error, setError] = useState<string | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const startedAtRef = useRef(0);
  const timerRef = useRef<number | null>(null);

  useEffect(() => () => {
    if (timerRef.current) window.clearInterval(timerRef.current);
    streamRef.current?.getTracks().forEach((track) => track.stop());
  }, []);
  useEffect(() => () => { if (previewUrl) URL.revokeObjectURL(previewUrl); }, [previewUrl]);

  async function start() {
    setError(null);
    const format = pickRecorderFormat();
    if (!format || !navigator.mediaDevices?.getUserMedia) {
      setError("Bu tarayıcı ses kaydını desteklemiyor. Telefonda Chrome ya da Safari'nin güncel sürümünü dene.");
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: { channelCount: 1, echoCancellation: false, noiseSuppression: false, autoGainControl: false },
      });
      streamRef.current = stream;
      const recorder = new MediaRecorder(stream, { mimeType: format.mimeType, audioBitsPerSecond: format.bitsPerSecond });
      const chunks: Blob[] = [];
      recorder.ondataavailable = (event) => { if (event.data.size > 0) chunks.push(event.data); };
      recorder.onstop = () => {
        stream.getTracks().forEach((track) => track.stop());
        if (timerRef.current) window.clearInterval(timerRef.current);
        const recorded = new Blob(chunks, { type: recorder.mimeType || format.mimeType });
        setElapsed(Math.min(AUDIO_MAX_SECONDS, Math.max(1, Math.round((performance.now() - startedAtRef.current) / 1000))));
        setBlob(recorded);
        setPreviewUrl(URL.createObjectURL(recorded));
        setPhase("recorded");
      };
      recorderRef.current = recorder;
      startedAtRef.current = performance.now();
      setElapsed(0);
      recorder.start();
      setPhase("recording");
      timerRef.current = window.setInterval(() => {
        const seconds = Math.floor((performance.now() - startedAtRef.current) / 1000);
        setElapsed(seconds);
        if (seconds >= AUDIO_MAX_SECONDS && recorder.state === "recording") recorder.stop();
      }, 250);
    } catch {
      setError("Mikrofona erişilemedi. Tarayıcının mikrofon iznini açıp yeniden dene.");
    }
  }

  function stop() {
    if (recorderRef.current?.state === "recording") recorderRef.current.stop();
  }

  function discard() {
    setBlob(null);
    setPreviewUrl(null);
    setElapsed(0);
    setPhase("idle");
  }

  async function save() {
    if (!blob) return;
    setError(null);
    try {
      await upload.mutateAsync({ lessonId, blob, durationSeconds: elapsed, pieceTitle });
      setPhase("saved");
      setBlob(null);
      setPreviewUrl(null);
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Kayıt yüklenemedi.");
    }
  }

  if (isLoading) return <div className="border-t border-[var(--line)] bg-[var(--surface-muted)] p-4"><div className="skeleton h-20 rounded-xl" /></div>;

  if (data && !data.consent) {
    return (
      <div className="border-t border-[var(--line)] bg-[var(--surface-muted)] p-4 text-xs">
        <p className="rounded-xl bg-white p-3 font-semibold text-[var(--muted)]">
          {studentName} için ses kaydı rızası yok. Veli rızayı portaldaki Gelişim sekmesinden, yönetici de Gelişim ekranındaki Ses Defteri bölümünden açabilir.
        </p>
      </div>
    );
  }

  const quotaFull = data ? data.termUsed >= data.termQuota : false;
  const tooBig = blob ? blob.size > AUDIO_MAX_BYTES : false;

  return (
    <div className="space-y-3 border-t border-[var(--line)] bg-[var(--surface-muted)] p-4 text-xs">
      {phase === "saved" ? (
        <div className="flex flex-wrap items-center justify-between gap-2 rounded-xl bg-[var(--success-soft)] p-3 font-bold text-[var(--success-strong)]">
          <span className="flex items-center gap-2"><Icon name="check" className="h-4 w-4" /> Kayıt Ses Defteri&apos;ne eklendi. Veliye göstermek için Gelişim ekranından paylaşabilirsin.</span>
          <button type="button" onClick={discard} className="btn btn-quiet">Yeni kayıt</button>
        </div>
      ) : phase === "recorded" && previewUrl && blob ? (
        <>
          <audio src={previewUrl} controls className="w-full" />
          <p className="text-meta">{elapsed} sn · {formatBytes(blob.size)}{tooBig && " · sınırı aşıyor, daha kısa bir kayıt al"}</p>
          <label className="block"><span className="text-meta mb-1.5 block font-bold">Eser <span className="font-medium">· opsiyonel, yolculukta ilk/son kaydı eşleştirir</span></span>
            <input value={pieceTitle} onChange={(event) => setPieceTitle(event.target.value)} maxLength={200} className="field text-xs" placeholder="Örn. Für Elise" />
          </label>
          <div className="flex flex-wrap gap-2">
            <button type="button" onClick={save} disabled={upload.isPending || tooBig} className="btn btn-primary">{upload.isPending ? "Gönderiliyor…" : "Kaydet"}</button>
            <button type="button" onClick={discard} disabled={upload.isPending} className="btn btn-quiet">Sil, yeniden kaydet</button>
          </div>
        </>
      ) : (
        <div className="flex items-center gap-3 rounded-xl bg-white p-3">
          {phase === "recording" ? (
            <button type="button" onClick={stop} aria-label="Kaydı durdur" className="pressable grid h-12 w-12 shrink-0 place-items-center rounded-full bg-[var(--danger-strong)] text-white">
              <span className="h-4 w-4 rounded-[3px] bg-white" />
            </button>
          ) : (
            <button type="button" onClick={start} disabled={quotaFull} aria-label="Kaydı başlat" className="pressable grid h-12 w-12 shrink-0 place-items-center rounded-full bg-[var(--brand)] text-white disabled:opacity-40">
              <Icon name="mic" className="h-5 w-5" />
            </button>
          )}
          <div className="min-w-0 flex-1">
            {phase === "recording" ? (
              <>
                <p className="flex items-center gap-2 font-bold"><span className="h-2 w-2 animate-pulse rounded-full bg-[var(--danger-strong)]" /> Kaydediliyor · {elapsed} / {AUDIO_MAX_SECONDS} sn</p>
                <div className="mt-2 h-1.5 overflow-hidden rounded-full bg-[var(--surface-muted)]"><div className="h-full bg-[var(--danger-strong)] transition-[width]" style={{ width: `${Math.min(100, (elapsed / AUDIO_MAX_SECONDS) * 100)}%` }} /></div>
              </>
            ) : quotaFull ? (
              <p className="font-semibold text-[var(--muted)]">Bu dönemin {data?.termQuota} kayıtlık sınırı doldu. Gelişim ekranından eski bir kaydı silerek yer açabilirsin.</p>
            ) : (
              <>
                <p className="font-bold">Çalışın 20–60 saniyesini kaydet</p>
                <p className="text-meta mt-0.5">60 sn&apos;de kendiliğinden durur{data ? ` · bu dönem ${data.termUsed}/${data.termQuota}` : ""}</p>
              </>
            )}
          </div>
        </div>
      )}
      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] p-3 font-semibold text-[var(--danger-strong)]">{error}</p>}
    </div>
  );
}

// Öğrenci künyesi / Gelişim: kayıt listesi. Yönetici rızayı buradan açıp kapatır; öğretmen ve
// yönetici kaydı veliye açar, yıldızlar (budamadan korunur) ya da siler.
export function StudentAudioClipsPanel({ studentId, isAdmin }: { studentId: string; isAdmin: boolean }) {
  const { data, isLoading, isError } = useStudentAudioClips(studentId);
  const setConsent = useSetAudioConsent(studentId, "admin");
  const [error, setError] = useState<string | null>(null);

  async function toggleConsent(next: boolean) {
    setError(null);
    if (!next && data?.clips.length && !window.confirm(`Rıza kapatılırsa bu öğrencinin ${data.clips.length} ses kaydı kalıcı olarak silinir. Devam edilsin mi?`)) return;
    try { await setConsent.mutateAsync(next); } catch (err) { setError(err instanceof ApiError ? err.detail ?? err.title : "Rıza güncellenemedi."); }
  }

  if (isLoading) return <div className="skeleton h-24 rounded-2xl" />;
  if (isError || !data) return <p className="text-meta">Ses Defteri yüklenemedi.</p>;

  return (
    <section className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <h3 className="flex items-center gap-2 text-sm font-bold"><Icon name="mic" className="h-4 w-4" /> Ses Defteri</h3>
          <p className="text-meta mt-0.5">Bu dönem {data.termUsed}/{data.termQuota} kayıt · eser başına ilk ve son kayıt ile yıldızlılar kalıcıdır</p>
        </div>
        {isAdmin ? (
          <label className="flex items-center gap-2 text-xs font-bold">
            <input type="checkbox" checked={data.consent} disabled={setConsent.isPending} onChange={(event) => void toggleConsent(event.target.checked)} />
            Veli ses kaydı rızası verdi
          </label>
        ) : (
          <span className={`rounded-full px-2 py-1 text-[.75rem] font-bold ${data.consent ? "bg-[var(--success-soft)] text-[var(--success-strong)]" : "bg-[var(--warning-soft)] text-[var(--warning-strong)]"}`}>{data.consent ? "Rıza var" : "Rıza yok"}</span>
        )}
      </div>
      {error && <p role="alert" className="rounded-xl bg-[var(--danger-soft)] p-3 text-xs font-semibold text-[var(--danger-strong)]">{error}</p>}
      {data.clips.length === 0 ? (
        <p className="rounded-xl border border-dashed border-[var(--line)] p-4 text-center text-xs text-[var(--muted)]">
          {data.consent ? "Henüz kayıt yok. Bugünkü derslerim kartındaki Kayıt düğmesiyle ilk kaydı al." : "Rıza açılmadan kayıt alınamaz."}
        </p>
      ) : (
        <ul className="space-y-2">{data.clips.map((clip) => <ClipRow key={clip.id} clip={clip} studentId={studentId} />)}</ul>
      )}
    </section>
  );
}

function ClipRow({ clip, studentId }: { clip: AudioClip; studentId: string }) {
  const share = useShareAudioClip(studentId);
  const star = useStarAudioClip(studentId);
  const remove = useDeleteAudioClip(studentId);
  const date = new Date(clip.createdAt);
  return (
    <li className="rounded-xl border border-[var(--line)] bg-white p-3">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="truncate text-sm font-bold">{clip.pieceTitle ?? "Eser belirtilmedi"}</p>
          <p className="text-meta">{date.toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" })} · {clip.durationSeconds} sn · {formatBytes(clip.sizeBytes)}{clip.teacherName && ` · ${clip.teacherName}`}</p>
        </div>
        <div className="flex items-center gap-1">
          <button type="button" onClick={() => star.mutate({ clipId: clip.id, starred: !clip.starred })} aria-pressed={clip.starred} aria-label={clip.starred ? "Yıldızı kaldır" : "Yıldızla (kalıcı tut)"} title={clip.starred ? "Yıldızı kaldır" : "Yıldızla: budamadan korunur"} className={`icon-btn icon-btn-quiet ${clip.starred ? "text-[#c58a00]" : ""}`}>
            <Icon name="star" className="h-4 w-4" fill={clip.starred ? "currentColor" : "none"} />
          </button>
          <button type="button" onClick={() => { if (window.confirm("Bu kayıt kalıcı olarak silinsin mi?")) remove.mutate(clip.id); }} aria-label="Kaydı sil" title="Sil" className="icon-btn icon-btn-quiet">
            <Icon name="trash" className="h-4 w-4" />
          </button>
        </div>
      </div>
      {/* preload="none": liste açılınca hiçbir ses indirilmez, yalnızca oynatılan iner. */}
      <audio src={staffClipUrl(clip.id)} controls preload="none" className="mt-2 w-full" />
      <label className="mt-2 flex items-center gap-2 text-xs font-semibold">
        <input type="checkbox" checked={clip.sharedWithGuardian} disabled={share.isPending} onChange={(event) => share.mutate({ clipId: clip.id, shared: event.target.checked })} />
        Veliye göster
      </label>
    </li>
  );
}
