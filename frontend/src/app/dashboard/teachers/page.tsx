"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { useCreateTeacher, useInstruments, useTeachers } from "@/lib/people";

export default function TeachersPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: teachers, isLoading } = useTeachers();
  const { data: instruments } = useInstruments();

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Öğretmenler</h1>

      {isAdmin && <CreateTeacherForm instruments={instruments ?? []} />}

      <div className="overflow-hidden rounded-lg border border-neutral-200 bg-white">
        {isLoading && <p className="p-4 text-sm text-neutral-500">Yükleniyor…</p>}
        {teachers?.length === 0 && <p className="p-4 text-sm text-neutral-500">Henüz öğretmen yok.</p>}
        <ul className="divide-y divide-neutral-200">
          {teachers?.map((teacher) => (
            <li id={`teacher-${teacher.id}`} key={teacher.id} className="scroll-mt-24 flex items-center justify-between px-4 py-3 text-sm target:bg-[#f0edff]">
              <span>
                {teacher.firstName} {teacher.lastName}
                <span className="ml-2 text-neutral-400">
                  {teacher.instrumentIds
                    .map((id) => instruments?.find((i) => i.id === id)?.name)
                    .filter(Boolean)
                    .join(", ")}
                </span>
              </span>
              <span className="text-xs text-neutral-400">
                {teacher.status === "Active" ? "aktif" : "pasif"}
                {teacher.hasLoginAccount ? " · giriş hesabı var" : " · giriş hesabı yok"}
              </span>
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function CreateTeacherForm({ instruments }: { instruments: { id: string; name: string }[] }) {
  const createTeacher = useCreateTeacher();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [email, setEmail] = useState("");
  const [selectedInstruments, setSelectedInstruments] = useState<string[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [temporaryPassword, setTemporaryPassword] = useState<string | null>(null);

  function toggleInstrument(id: string) {
    setSelectedInstruments((prev) => (prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id]));
  }

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    setTemporaryPassword(null);
    try {
      const result = await createTeacher.mutateAsync({
        firstName,
        lastName,
        instrumentIds: selectedInstruments,
        email: email || undefined,
      });
      setFirstName("");
      setLastName("");
      setEmail("");
      setSelectedInstruments([]);
      if (result.temporaryPassword) {
        setTemporaryPassword(result.temporaryPassword);
      }
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğretmen eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3 rounded-lg border border-neutral-200 bg-white p-4">
      <div className="flex flex-wrap items-end gap-2">
        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Ad</label>
          <input value={firstName} onChange={(e) => setFirstName(e.target.value)} required
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" />
        </div>
        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">Soyad</label>
          <input value={lastName} onChange={(e) => setLastName(e.target.value)} required
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" />
        </div>
        <div className="space-y-1">
          <label className="text-xs font-medium text-neutral-600">E-posta (giriş hesabı için, opsiyonel)</label>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)}
            className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" />
        </div>
      </div>

      <div className="space-y-1">
        <label className="text-xs font-medium text-neutral-600">Enstrümanlar</label>
        <div className="flex flex-wrap gap-2">
          {instruments.map((i) => (
            <label key={i.id} className="flex items-center gap-1 text-sm">
              <input
                type="checkbox"
                checked={selectedInstruments.includes(i.id)}
                onChange={() => toggleInstrument(i.id)}
              />
              {i.name}
            </label>
          ))}
        </div>
      </div>

      <button type="submit" disabled={createTeacher.isPending}
        className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
        {createTeacher.isPending ? "Ekleniyor…" : "Öğretmen ekle"}
      </button>

      {error && <p className="text-sm text-red-600">{error}</p>}

      {temporaryPassword && (
        <p className="rounded-md border border-amber-300 bg-amber-50 p-2 text-sm text-amber-900">
          Geçici şifre: <code className="font-mono">{temporaryPassword}</code> — bunu öğretmene sözlü/WhatsApp ile
          ilet, bir daha gösterilmeyecek.
        </p>
      )}
    </form>
  );
}
