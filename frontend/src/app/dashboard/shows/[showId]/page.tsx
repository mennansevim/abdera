"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useMemo, useRef, useState, type FormEvent } from "react";
import { Icon } from "@/components/icons";
import { FormActions, FormMessage, Modal, PageHeader } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { useInstruments, useStudents, useTeachers } from "@/lib/people";
import {
  SHOW_ITEM_KIND_LABEL,
  SHOW_STATUS_LABEL,
  studentPhotoUrl,
  useAddShowItem,
  useDeleteShowItem,
  useReorderShowItems,
  useShow,
  useUpdateShowItem,
  useUploadStudentPhoto,
  type ShowItem,
  type ShowItemInput,
  type ShowItemKind,
} from "@/lib/shows";

// Program ekranı: gösterinin sıralı akışı. Resital yazılımlarının (Pembee, RecitalDash,
// CompuDance) ortak yüzeyi - sıra numarası, öğrenci, enstrüman, eser + besteci, öğretmen,
// süre ve basılabilir seyirci programı.
//
// Sıralama tek boyutlu tutuldu: bölüm (GroupName) yalnızca bir başlık, ayrı bir kap değil.
// Böylece sürükle-bırak "listede yukarı/aşağı"dan ibaret kalıyor; bölümler arası taşıma
// diye ikinci bir kavram öğrenmek gerekmiyor.

function formatDateTime(value: string) {
  return new Date(value).toLocaleString("tr-TR", {
    day: "numeric", month: "long", year: "numeric", hour: "2-digit", minute: "2-digit",
  });
}

function formatDuration(minutes: number) {
  if (minutes <= 0) return "—";
  const hours = Math.floor(minutes / 60);
  const rest = minutes % 60;
  return hours ? `${hours} sa ${rest} dk` : `${rest} dk`;
}

function emptyItem(groupName: string | null): ShowItemInput {
  return { kind: "Performance", groupName, studentId: "", instrumentId: "", teacherId: "", pieceTitle: "", composer: "", durationMinutes: 4, note: "" };
}

export default function ShowProgramPage() {
  const params = useParams<{ showId: string }>();
  const showId = params.showId;
  const { data: me } = useMe();
  const { data: show, isLoading, isError } = useShow(showId);
  const reorder = useReorderShowItems(showId);
  const [reorderError, setReorderError] = useState<string | null>(null);
  const [editing, setEditing] = useState<ShowItem | null>(null);
  const [adding, setAdding] = useState<ShowItemInput | null>(null);
  const isAdmin = me?.role === "Admin";

  // Bölüm başlıkları listenin kendisinden türetilir - grup adı değişen ilk satırda
  // yeni bir başlık basılır. Ayrı bir "bölümler" state'i tutmak, sıralama ile başlıkların
  // ayrışabildiği bir ikinci doğruluk kaynağı yaratırdı.
  const rows = useMemo(() => {
    const result: Array<{ kind: "header"; label: string } | { kind: "item"; item: ShowItem; index: number }> = [];
    let lastGroup: string | null | undefined;
    (show?.items ?? []).forEach((item, index) => {
      if (item.groupName !== lastGroup) {
        lastGroup = item.groupName;
        if (item.groupName) result.push({ kind: "header", label: item.groupName });
      }
      result.push({ kind: "item", item, index });
    });
    return result;
  }, [show?.items]);

  const knownGroups = useMemo(
    () => [...new Set((show?.items ?? []).map((item) => item.groupName).filter((name): name is string => !!name))],
    [show?.items],
  );

  function move(index: number, delta: number) {
    const items = show?.items ?? [];
    const target = index + delta;
    if (target < 0 || target >= items.length) return;
    const ids = items.map((item) => item.id);
    [ids[index], ids[target]] = [ids[target], ids[index]];
    saveOrder(ids);
  }

  function dropOn(fromIndex: number, toIndex: number) {
    if (fromIndex === toIndex) return;
    const ids = (show?.items ?? []).map((item) => item.id);
    const [moved] = ids.splice(fromIndex, 1);
    ids.splice(toIndex, 0, moved);
    saveOrder(ids);
  }

  function saveOrder(ids: string[]) {
    setReorderError(null);
    reorder.mutateAsync(ids).catch((err: unknown) =>
      setReorderError(err instanceof ApiError ? (err.detail ?? err.title) : "Sıra değiştirilemedi."));
  }

  if (isLoading) return <div className="space-y-3"><div className="skeleton h-16 rounded-2xl" /><div className="skeleton h-72 rounded-2xl" /></div>;
  if (!show) {
    return (
      <p role={isError ? "alert" : undefined} className="app-card p-6 text-sm text-[var(--muted)]">
        {isError ? "Gösteri yüklenemedi. Bağlantınızı kontrol edip sayfayı yenileyin." : "Gösteri bulunamadı."}
      </p>
    );
  }

  return (
    <div className="space-y-4">
      <div className="print:hidden">
        <PageHeader
          title={show.title}
          description={`${formatDateTime(show.startsAt)}${show.venueName ? ` · ${show.venueName}` : ""} · ${SHOW_STATUS_LABEL[show.status]}`}
          actions={
            <div className="flex flex-wrap gap-2">
              <Link href="/dashboard/shows" className="btn btn-quiet"><Icon name="arrow-left" className="h-4 w-4" />Gösteriler</Link>
              <button type="button" onClick={() => window.print()} className="btn btn-quiet">Programı yazdır</button>
              <Link href={`/dashboard/shows/${showId}/stage`} className="btn btn-primary"><Icon name="music" className="h-4 w-4" />Sahne ekranı</Link>
            </div>
          }
        />
      </div>

      {/* Baskıda yalnızca bu başlık ve aşağıdaki liste görünür - seyirci programı. */}
      <header className="hidden print:block">
        <h1 className="font-serif text-2xl font-bold">{show.title}</h1>
        <p className="text-sm">{formatDateTime(show.startsAt)}{show.venueName ? ` · ${show.venueName}` : ""}</p>
      </header>

      <section className="grid gap-3 sm:grid-cols-3 print:hidden" aria-label="Program özeti">
        {[
          { label: "Sıra", value: `${show.items.length}`, detail: "programdaki toplam satır" },
          { label: "Sahneye çıkan", value: `${new Set(show.items.filter((item) => item.studentId).map((item) => item.studentId)).size}`, detail: "farklı öğrenci" },
          { label: "Tahmini süre", value: formatDuration(show.totalDurationMinutes), detail: "girilen sürelerin toplamı" },
        ].map((tile) => (
          <article key={tile.label} className="app-card p-4">
            <p className="text-[.75rem] font-bold text-[var(--muted)]">{tile.label}</p>
            <p className="mt-1 text-lg font-bold tabular-nums">{tile.value}</p>
            <p className="text-meta mt-0.5">{tile.detail}</p>
          </article>
        ))}
      </section>

      <section className="app-card overflow-hidden">
        <div className="flex flex-wrap items-center justify-between gap-2 border-b border-[var(--line)] bg-[var(--surface-muted)]/60 px-4 py-3 print:hidden">
          <div>
            <h2 className="text-title">Program</h2>
            <p className="text-meta mt-0.5">Sürükleyerek veya okları kullanarak sırayı değiştir.</p>
          </div>
          {isAdmin && (
            <div className="flex flex-wrap gap-2">
              <button type="button" onClick={() => setAdding({ ...emptyItem(knownGroups.at(-1) ?? "1. Bölüm"), kind: "Intermission", pieceTitle: "Ara", durationMinutes: 15 })} className="btn btn-quiet">Ara ekle</button>
              <button type="button" onClick={() => setAdding(emptyItem(knownGroups.at(-1) ?? "1. Bölüm"))} className="btn btn-primary"><Icon name="plus" className="h-4 w-4" />Sıra ekle</button>
            </div>
          )}
        </div>

        {reorderError && <div className="px-4 pt-3 print:hidden"><FormMessage tone="error">{reorderError}</FormMessage></div>}

        {show.items.length === 0 && (
          <div className="grid min-h-48 place-items-center p-8 text-center">
            <div>
              <p className="text-sm font-bold">Program boş</p>
              <p className="text-meta mt-1">İlk sırayı ekleyerek başla: öğrenci, enstrüman ve çalacağı eser.</p>
              {isAdmin && <button type="button" onClick={() => setAdding(emptyItem("1. Bölüm"))} className="btn btn-primary mt-3">Sıra ekle</button>}
            </div>
          </div>
        )}

        <ol className="divide-y divide-[var(--line)]">
          {rows.map((row) => row.kind === "header"
            ? <li key={`header-${row.label}`} className="bg-[var(--brand-soft)]/40 px-4 py-2 font-serif text-sm font-bold text-[var(--brand-strong)] print:bg-transparent print:px-0">{row.label}</li>
            : <ProgramRow
                key={row.item.id}
                showId={showId}
                item={row.item}
                index={row.index}
                total={show.items.length}
                isAdmin={!!isAdmin}
                isCurrent={show.currentItemId === row.item.id}
                onMove={move}
                onDrop={dropOn}
                onEdit={() => setEditing(row.item)}
              />)}
        </ol>
      </section>

      {adding && <ItemModal showId={showId} initial={adding} knownGroups={knownGroups} onClose={() => setAdding(null)} />}
      {editing && <ItemModal showId={showId} initial={editing} itemId={editing.id} knownGroups={knownGroups} onClose={() => setEditing(null)} />}
    </div>
  );
}

function ProgramRow({
  showId, item, index, total, isAdmin, isCurrent, onMove, onDrop, onEdit,
}: {
  showId: string;
  item: ShowItem;
  index: number;
  total: number;
  isAdmin: boolean;
  isCurrent: boolean;
  onMove: (index: number, delta: number) => void;
  onDrop: (from: number, to: number) => void;
  onEdit: () => void;
}) {
  const deleteItem = useDeleteShowItem(showId);
  const [dragOver, setDragOver] = useState(false);
  const [confirmingDelete, setConfirmingDelete] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);

  const isPerformance = item.kind === "Performance";

  return (
    <li
      draggable={isAdmin}
      onDragStart={(event) => event.dataTransfer.setData("text/plain", String(index))}
      onDragOver={(event) => { if (isAdmin) { event.preventDefault(); setDragOver(true); } }}
      onDragLeave={() => setDragOver(false)}
      onDrop={(event) => {
        event.preventDefault();
        setDragOver(false);
        const from = Number(event.dataTransfer.getData("text/plain"));
        if (Number.isFinite(from)) onDrop(from, index);
      }}
      className={`flex flex-wrap items-center gap-3 px-4 py-3 print:px-0 ${dragOver ? "bg-[var(--brand-soft)]/60" : ""} ${isCurrent ? "bg-[var(--danger-soft)]/40" : ""}`}
    >
      <span className="w-7 shrink-0 text-center font-serif text-sm font-bold tabular-nums text-[var(--muted)]">{index + 1}</span>

      {isPerformance ? (
        <span className="grid h-11 w-11 shrink-0 place-items-center overflow-hidden rounded-xl bg-[var(--brand-soft)] text-[.75rem] font-bold text-[var(--brand-strong)]">
          {item.hasPhoto && item.studentId
            /* eslint-disable-next-line @next/next/no-img-element */
            ? <img src={studentPhotoUrl(item.studentId, item.photoVersion)} alt="" className="h-full w-full object-cover" />
            : (item.studentName ?? "?").split(" ").map((part) => part[0]).slice(0, 2).join("")}
        </span>
      ) : (
        <span className="grid h-11 w-11 shrink-0 place-items-center rounded-xl bg-[var(--surface-muted)] text-[var(--muted)] print:hidden">
          <Icon name={item.kind === "Intermission" ? "clock" : "bell"} className="h-4 w-4" />
        </span>
      )}

      <div className="min-w-0 flex-1 basis-32">
        {isPerformance ? (
          <>
            <p className="truncate text-sm font-bold">{item.studentName}</p>
            <p className="text-meta truncate">
              <strong className="font-semibold text-[var(--foreground)]">{item.pieceTitle}</strong>
              {item.composer ? ` · ${item.composer}` : ""}
              {item.instrumentName ? ` · ${item.instrumentName}` : ""}
              {item.teacherName ? ` · ${item.teacherName}` : ""}
            </p>
          </>
        ) : (
          <>
            <p className="truncate text-sm font-bold">{item.pieceTitle ?? SHOW_ITEM_KIND_LABEL[item.kind]}</p>
            {item.note && <p className="text-meta truncate">{item.note}</p>}
          </>
        )}
      </div>

      <span className="shrink-0 text-[.75rem] font-semibold tabular-nums text-[var(--muted)]">
        {item.durationMinutes ? `${item.durationMinutes} dk` : "—"}
      </span>

      {isAdmin && (
        <div className="flex shrink-0 basis-full items-center justify-end gap-1 sm:basis-auto print:hidden">
          <button type="button" onClick={() => onMove(index, -1)} disabled={index === 0} aria-label="Yukarı taşı" className="icon-btn icon-btn-quiet disabled:opacity-30"><Icon name="chevron" className="h-3.5 w-3.5 -rotate-90" /></button>
          <button type="button" onClick={() => onMove(index, 1)} disabled={index === total - 1} aria-label="Aşağı taşı" className="icon-btn icon-btn-quiet disabled:opacity-30"><Icon name="chevron" className="h-3.5 w-3.5 rotate-90" /></button>
          <button type="button" onClick={onEdit} aria-label="Düzenle" className="icon-btn icon-btn-quiet"><Icon name="pencil" className="h-3.5 w-3.5" /></button>
          <button
            type="button"
            onClick={() => setConfirmingDelete(true)}
            aria-label="Programdan çıkar"
            className="icon-btn icon-btn-quiet hover:border-[var(--danger)] hover:text-[var(--danger-strong)]"
          >
            <Icon name="x" className="h-3.5 w-3.5" />
          </button>
        </div>
      )}

      {confirmingDelete && (
        <Modal open title="Sıra programdan çıkarılsın mı?" onClose={() => { setConfirmingDelete(false); setDeleteError(null); }} size="sm">
          <form
            onSubmit={(event) => {
              event.preventDefault();
              setDeleteError(null);
              deleteItem.mutateAsync(item.id)
                .then(() => setConfirmingDelete(false))
                .catch((err: unknown) => setDeleteError(err instanceof ApiError ? (err.detail ?? err.title) : "Sıra çıkarılamadı."));
            }}
            className="space-y-3.5"
          >
            <p className="text-sm text-[var(--muted)]">{index + 1}. sıra programdan çıkarılacak.</p>
            {deleteError && <FormMessage tone="error">{deleteError}</FormMessage>}
            <FormActions onCancel={() => setConfirmingDelete(false)} submitLabel="Çıkar" pending={deleteItem.isPending} pendingLabel="Çıkarılıyor…" />
          </form>
        </Modal>
      )}
    </li>
  );
}

function ItemModal({
  showId, initial, itemId, knownGroups, onClose,
}: {
  showId: string;
  initial: ShowItemInput | ShowItem;
  itemId?: string;
  knownGroups: string[];
  onClose: () => void;
}) {
  const addItem = useAddShowItem(showId);
  const updateItem = useUpdateShowItem(showId);
  const { data: students } = useStudents();
  const { data: instruments } = useInstruments();
  const { data: teachers } = useTeachers();

  const [draft, setDraft] = useState<ShowItemInput>({
    kind: initial.kind,
    groupName: initial.groupName ?? "",
    studentId: initial.studentId ?? "",
    instrumentId: initial.instrumentId ?? "",
    teacherId: initial.teacherId ?? "",
    pieceTitle: initial.pieceTitle ?? "",
    composer: initial.composer ?? "",
    durationMinutes: initial.durationMinutes ?? null,
    note: initial.note ?? "",
  });
  const [error, setError] = useState<string | null>(null);
  const pending = addItem.isPending || updateItem.isPending;
  const isPerformance = draft.kind === "Performance";

  function patch(next: Partial<ShowItemInput>) {
    setDraft((current) => ({ ...current, ...next }));
    setError(null);
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    const body: ShowItemInput = {
      ...draft,
      groupName: draft.groupName?.trim() || null,
      studentId: isPerformance ? draft.studentId || null : null,
      instrumentId: isPerformance ? draft.instrumentId || null : null,
      teacherId: isPerformance ? draft.teacherId || null : null,
      pieceTitle: draft.pieceTitle?.trim() || null,
      composer: isPerformance ? draft.composer?.trim() || null : null,
      note: draft.note?.trim() || null,
    };
    try {
      if (itemId) await updateItem.mutateAsync({ itemId, ...body });
      else await addItem.mutateAsync(body);
      onClose();
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Sıra kaydedilemedi.");
    }
  }

  return (
    <Modal
      open
      title={itemId ? "Sırayı düzenle" : "Programa sıra ekle"}
      description="Bölüm başlığı aynı kalan satırlar programda tek başlık altında toplanır."
      onClose={onClose}
    >
      <form onSubmit={handleSubmit} className="space-y-3.5">
        <div className="grid gap-3 sm:grid-cols-2">
          <label className="form-label">Sıra türü
            <select value={draft.kind} onChange={(event) => patch({ kind: event.target.value as ShowItemKind })} className="field text-sm">
              <option value="Performance">Sahne sırası</option>
              <option value="Intermission">Ara</option>
              <option value="Announcement">Duyuru</option>
            </select>
          </label>
          <label className="form-label">Bölüm
            <input list="show-groups" value={draft.groupName ?? ""} onChange={(event) => patch({ groupName: event.target.value })} maxLength={200} className="field text-sm" placeholder="1. Bölüm" />
            <datalist id="show-groups">{knownGroups.map((group) => <option key={group} value={group} />)}</datalist>
          </label>
        </div>

        {isPerformance && (
          <>
            <label className="form-label">Öğrenci
              <select value={draft.studentId ?? ""} onChange={(event) => patch({ studentId: event.target.value })} required className="field text-sm">
                <option value="">Öğrenci seç…</option>
                {students?.filter((student) => student.status === "Active").map((student) => (
                  <option key={student.id} value={student.id}>{student.firstName} {student.lastName}</option>
                ))}
              </select>
            </label>

            {draft.studentId && <PhotoField studentId={draft.studentId} />}

            <div className="grid gap-3 sm:grid-cols-2">
              <label className="form-label">Enstrüman
                <select value={draft.instrumentId ?? ""} onChange={(event) => patch({ instrumentId: event.target.value })} className="field text-sm">
                  <option value="">Seç…</option>
                  {instruments?.map((instrument) => <option key={instrument.id} value={instrument.id}>{instrument.name}</option>)}
                </select>
              </label>
              <label className="form-label">Öğretmen
                <select value={draft.teacherId ?? ""} onChange={(event) => patch({ teacherId: event.target.value })} className="field text-sm">
                  <option value="">Seç…</option>
                  {teachers?.filter((teacher) => teacher.status === "Active").map((teacher) => (
                    <option key={teacher.id} value={teacher.id}>{teacher.firstName} {teacher.lastName}</option>
                  ))}
                </select>
              </label>
            </div>
          </>
        )}

        <div className="grid gap-3 sm:grid-cols-[2fr_1.5fr_auto]">
          <label className="form-label">{isPerformance ? "Eser" : "Başlık"}
            <input value={draft.pieceTitle ?? ""} onChange={(event) => patch({ pieceTitle: event.target.value })} required={isPerformance} maxLength={200} className="field text-sm" placeholder={isPerformance ? "Für Elise" : "Ara"} />
          </label>
          {isPerformance && (
            <label className="form-label">Besteci
              <input value={draft.composer ?? ""} onChange={(event) => patch({ composer: event.target.value })} maxLength={200} className="field text-sm" placeholder="Beethoven" />
            </label>
          )}
          <label className="form-label">Süre (dk)
            <input type="number" min={1} max={120} value={draft.durationMinutes ?? ""} onChange={(event) => patch({ durationMinutes: event.target.value === "" ? null : Number(event.target.value) })} className="field w-24 text-sm" />
          </label>
        </div>

        <label className="form-label">Not
          <input value={draft.note ?? ""} onChange={(event) => patch({ note: event.target.value })} maxLength={200} className="field text-sm" placeholder="Kuliste hatırlatılacak bir şey" />
        </label>

        {error && <FormMessage tone="error">{error}</FormMessage>}
        <FormActions onCancel={onClose} submitLabel={itemId ? "Kaydet" : "Programa ekle"} pending={pending} pendingLabel="Kaydediliyor…" />
      </form>
    </Modal>
  );
}

// Fotoğraf sahne ekranının "sırada kim var" göstergesi. Buradan yüklenmesi bilinçli:
// programı kuran kişi zaten öğrenciyi seçmiş oluyor, ayrı bir ekrana gitmesi gerekmiyor.
function PhotoField({ studentId }: { studentId: string }) {
  const upload = useUploadStudentPhoto(studentId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [version, setVersion] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [broken, setBroken] = useState(false);

  async function handleChange(event: React.ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0];
    if (!file) return;
    setError(null);
    try {
      const result = await upload.mutateAsync(file);
      setVersion(result.version);
      setBroken(false);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Fotoğraf yüklenemedi.");
    } finally {
      if (inputRef.current) inputRef.current.value = "";
    }
  }

  return (
    <div className="flex items-center gap-3 rounded-xl border border-[var(--line)] p-3">
      <span className="grid h-14 w-14 shrink-0 place-items-center overflow-hidden rounded-xl bg-[var(--surface-muted)] text-[var(--muted)]">
        {broken
          ? <Icon name="students" className="h-5 w-5" />
          /* eslint-disable-next-line @next/next/no-img-element */
          : <img src={studentPhotoUrl(studentId, version)} alt="" onError={() => setBroken(true)} className="h-full w-full object-cover" />}
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-xs font-bold">Fotoğraf</p>
        <p className="text-meta">Sahne ekranında sıradaki öğrenciyi göstermek için. JPEG/PNG/WebP, en fazla 2 MB.</p>
        {error && <p className="mt-1 text-[.75rem] font-semibold text-[var(--danger-strong)]">{error}</p>}
      </div>
      <input ref={inputRef} type="file" accept="image/jpeg,image/png,image/webp" onChange={handleChange} className="sr-only" id={`photo-${studentId}`} />
      <label htmlFor={`photo-${studentId}`} className="btn btn-quiet shrink-0 cursor-pointer">
        {upload.isPending ? "Yükleniyor…" : "Yükle"}
      </label>
    </div>
  );
}
