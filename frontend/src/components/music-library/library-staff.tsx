"use client";

import { useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import {
  SCORE_FILE_MAX_BYTES, errorMessage, scoreFileUrl, useCreateLibraryPiece, useDeleteLibraryPiece,
  useSuggestToStudent, useUploadScoreFile, type LibraryPiece,
} from "@/lib/library";
import { useStudents } from "@/lib/people";
import { categories, instruments, learningSteps, type CustomPiece, type SheetMusic } from "@/lib/sheet-music";
import css from "./music-library.module.css";

// Kütüphanenin yalnızca panelde (öğretmen/yönetici) görünen parçaları. Herkese açık /kutuphane
// sayfası bunları hiç çizmez; yetki kuralı ayrıca API'de uygulanır.

function pdfProblem(file: File | null) {
  if (!file) return null;
  if (file.type && file.type !== "application/pdf") return "Yalnızca PDF dosyası yüklenebilir.";
  if (file.size > SCORE_FILE_MAX_BYTES) return "PDF en fazla 4 MB olabilir.";
  return null;
}

export function AddPieceForm({ defaultInstrument, onDone, onCancel }: { defaultInstrument: string; onDone: (piece: LibraryPiece) => void; onCancel: () => void }) {
  const create = useCreateLibraryPiece();
  const upload = useUploadScoreFile();
  const [title, setTitle] = useState("");
  const [composer, setComposer] = useState("");
  const [instrument, setInstrument] = useState(defaultInstrument);
  const [category, setCategory] = useState("education");
  const [level, setLevel] = useState("");
  const [notes, setNotes] = useState("");
  const [file, setFile] = useState<File | null>(null);
  const [error, setError] = useState("");
  const busy = create.isPending || upload.isPending;

  async function submit(event: FormEvent) {
    event.preventDefault();
    const fileError = pdfProblem(file);
    if (fileError) { setError(fileError); return; }
    setError("");
    try {
      const piece = await create.mutateAsync({ title, composer, instrument, category, level: level ? Number(level) : null, notes });
      if (file) {
        try { await upload.mutateAsync({ entryId: piece.entryId, file }); }
        catch (err) {
          // Eser kaydedildi; PDF'i ayrıntı ekranından tekrar yüklemek mümkün.
          setError(`Eser eklendi ama PDF yüklenemedi: ${errorMessage(err, "bilinmeyen hata")}`);
          onDone(piece);
          return;
        }
      }
      onDone(piece);
    } catch (err) {
      setError(errorMessage(err, "Eser eklenemedi."));
    }
  }

  return <form className={css.detail} onSubmit={submit}>
    <div className={css.detailTop}><span>Kütüphaneye eser ekle</span><button type="button" onClick={onCancel} aria-label="Formu kapat"><Icon name="close" /></button></div>
    <p className={css.explanation}>Eklediğin eser yalnızca okulun öğretmen ve yöneticilerine görünür. Eseri sen ve yönetici düzenleyip silebilir.</p>
    <div className={css.form}>
      <label>Eser adı<input required maxLength={200} value={title} onChange={e => setTitle(e.target.value)} placeholder="ör. Rock groove 1" /></label>
      <label>Besteci / kaynak<input maxLength={200} value={composer} onChange={e => setComposer(e.target.value)} placeholder="ör. Okul çalışması" /></label>
      <label>Enstrüman<select value={instrument} onChange={e => setInstrument(e.target.value)}>{Object.entries(instruments).filter(([key]) => key !== "all").map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      <label>Koleksiyon<select value={category} onChange={e => setCategory(e.target.value)}>{Object.entries(categories).filter(([key]) => key !== "all").map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
      <label>Seviye önerisi<select value={level} onChange={e => setLevel(e.target.value)}><option value="">Belirtme</option>{learningSteps.map((step, i) => <option key={step.name} value={i + 1}>{i + 1} · {step.name}</option>)}</select></label>
      <label>Nota (PDF, en fazla 4 MB)<input type="file" accept="application/pdf" onChange={e => setFile(e.target.files?.[0] ?? null)} /></label>
      <label className={css.wide}>Not<textarea maxLength={1000} rows={3} value={notes} onChange={e => setNotes(e.target.value)} placeholder="Çalışma notu, tempo, kaynak kitap…" /></label>
    </div>
    {error && <p className={css.formError} role="alert">{error}</p>}
    <div className={css.detailActions}>
      <button type="submit" className={css.primary} disabled={busy}><Icon name="plus" />{busy ? "Kaydediliyor…" : "Eseri ekle"}</button>
      <button type="button" onClick={onCancel}>Vazgeç</button>
    </div>
  </form>;
}

export function CustomPieceFile({ piece, scoreFile }: { piece: CustomPiece; scoreFile?: { version: string } }) {
  const upload = useUploadScoreFile();
  const remove = useDeleteLibraryPiece();
  const [preview, setPreview] = useState(false);
  const [error, setError] = useState("");
  const url = scoreFile ? scoreFileUrl(piece.id, scoreFile.version) : null;

  async function replace(file: File | null) {
    const problem = pdfProblem(file);
    if (!file || problem) { setError(problem ?? ""); return; }
    setError("");
    try { await upload.mutateAsync({ entryId: piece.id, file }); }
    catch (err) { setError(errorMessage(err, "PDF yüklenemedi.")); }
  }

  async function deletePiece() {
    if (!window.confirm(`"${piece.title}" kütüphaneden ve PDF'i ile birlikte silinsin mi? Öğrencilere yapılmış öneriler listelerinde kalır.`)) return;
    try { await remove.mutateAsync(piece.pieceId); }
    catch (err) { setError(errorMessage(err, "Eser silinemedi.")); }
  }

  return <>
    <div className={css.detailActions}>
      {url && <a className={css.primary} href={url} target="_blank" rel="noopener noreferrer"><Icon name="note" />Notayı aç (PDF)</a>}
      {url && <button aria-expanded={preview} onClick={() => setPreview(!preview)}>{preview ? "Önizlemeyi kapat" : "Burada önizle"}</button>}
      {piece.canEdit && <label className={css.fileButton}><Icon name="plus" />{upload.isPending ? "Yükleniyor…" : url ? "PDF'i değiştir" : "PDF yükle"}<input type="file" accept="application/pdf" disabled={upload.isPending} onChange={e => void replace(e.target.files?.[0] ?? null)} /></label>}
    </div>
    {url && preview && <div className={css.preview}><iframe src={url} title={`${piece.title} notası`} /><p>Önizleme görünmüyorsa <a href={url} target="_blank" rel="noopener noreferrer">notayı yeni sekmede aç.</a></p></div>}
    {!url && <p className={css.explanation}>Bu eserin PDF&apos;i henüz yüklenmedi.</p>}
    {piece.notes && <div className={css.practice}><h3>Not</h3><p>{piece.notes}</p></div>}
    {error && <p className={css.formError} role="alert">{error}</p>}
    {piece.canEdit && <button className={css.dangerLink} onClick={() => void deletePiece()} disabled={remove.isPending}>Eseri kütüphaneden sil</button>}
  </>;
}

// Öğretmenin listesi zaten yalnızca kendi öğrencileridir (GET /api/students öğretmene kendi
// kayıtlarını döner); sunucu yine de her öneride sahipliği ayrıca doğrular.
export function SuggestToStudent({ piece }: { piece: SheetMusic }) {
  const { data: students, isLoading } = useStudents();
  const suggest = useSuggestToStudent();
  const [open, setOpen] = useState(false);
  const [studentId, setStudentId] = useState("");
  const [note, setNote] = useState("");
  const [message, setMessage] = useState<{ ok: boolean; text: string } | null>(null);
  const active = (students ?? []).filter(student => student.status !== "Inactive");
  const chosen = studentId || active[0]?.id || "";

  async function submit(event: FormEvent) {
    event.preventDefault();
    const student = active.find(item => item.id === chosen);
    if (!student) return;
    try {
      await suggest.mutateAsync({ studentId: student.id, entryId: piece.id, title: piece.title, composer: piece.composer, note });
      setMessage({ ok: true, text: `${student.firstName} ${student.lastName} için önerildi. Gelişim ekranındaki "Önerilen eserler" listesinde görünür.` });
      setNote("");
    } catch (err) {
      setMessage({ ok: false, text: errorMessage(err, "Öneri eklenemedi.") });
    }
  }

  if (!open) return <button className={css.suggestToggle} onClick={() => { setOpen(true); setMessage(null); }}><Icon name="students" />Öğrenciye öner</button>;

  return <form className={css.suggest} onSubmit={submit}>
    <h3>Öğrenciye öner</h3>
    {isLoading ? <p className={css.explanation}>Öğrenciler yükleniyor…</p> : !active.length ? <p className={css.explanation}>Öneri yapabileceğin aktif öğrenci yok.</p> : <>
      <div className={css.form}>
        <label>Öğrenci<select value={chosen} onChange={e => { setStudentId(e.target.value); setMessage(null); }}>{active.map(student => <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>)}</select></label>
        <label>Not (isteğe bağlı)<input maxLength={500} value={note} onChange={e => setNote(e.target.value)} placeholder="ör. Önce ilk 8 ölçü" /></label>
      </div>
      <div className={css.detailActions}>
        <button type="submit" className={css.primary} disabled={suggest.isPending}>{suggest.isPending ? "Ekleniyor…" : "Öner"}</button>
        <button type="button" onClick={() => setOpen(false)}>Kapat</button>
      </div>
    </>}
    {message && <p className={message.ok ? css.notice : css.formError} role="status">{message.text}</p>}
  </form>;
}
