"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useMe } from "@/lib/use-auth";
import { useCreateStudent, useStudents } from "@/lib/people";
import { StudentDetail } from "./student-detail";

// docs/04-permissions.md: öğrenci oluşturma/veli/kayıt yönetimi yalnızca Admin - Teacher
// yalnızca kendisine atanmış öğrencileri görür, formlar 403 vermesin diye gizlenir.
export default function StudentsPage() {
  const { data: me } = useMe();
  const isAdmin = me?.role === "Admin";
  const { data: students, isLoading } = useStudents();
  const [expandedId, setExpandedId] = useState<string | null>(null);

  return (
    <div className="space-y-6">
      <h1 className="text-2xl font-semibold">Öğrenciler</h1>

      {isAdmin && <CreateStudentForm />}

      <div className="overflow-hidden rounded-lg border border-neutral-200 bg-white">
        {isLoading && <p className="p-4 text-sm text-neutral-500">Yükleniyor…</p>}
        {students?.length === 0 && <p className="p-4 text-sm text-neutral-500">Henüz öğrenci yok.</p>}
        <ul className="divide-y divide-neutral-200">
          {students?.map((student) => (
            <li key={student.id}>
              <button
                onClick={() => setExpandedId(expandedId === student.id ? null : student.id)}
                className="flex w-full items-center justify-between px-4 py-3 text-left text-sm hover:bg-neutral-50"
              >
                <span>
                  {student.firstName} {student.lastName}
                  <span className="ml-2 text-neutral-400">{student.birthDate}</span>
                </span>
                <span className="text-neutral-400">{expandedId === student.id ? "▲" : "▼"}</span>
              </button>
              {expandedId === student.id && <StudentDetail studentId={student.id} isAdmin={isAdmin} />}
            </li>
          ))}
        </ul>
      </div>
    </div>
  );
}

function CreateStudentForm() {
  const createStudent = useCreateStudent();
  const [firstName, setFirstName] = useState("");
  const [lastName, setLastName] = useState("");
  const [birthDate, setBirthDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await createStudent.mutateAsync({ firstName, lastName, birthDate });
      setFirstName("");
      setLastName("");
      setBirthDate("");
    } catch (err) {
      setError(err instanceof ApiError ? (err.detail ?? err.title) : "Öğrenci eklenemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="flex flex-wrap items-end gap-2 rounded-lg border border-neutral-200 bg-white p-4">
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
        <label className="text-xs font-medium text-neutral-600">Doğum tarihi</label>
        <input type="date" value={birthDate} onChange={(e) => setBirthDate(e.target.value)} required
          className="block rounded-md border border-neutral-300 px-2 py-1 text-sm" />
      </div>
      <button type="submit" disabled={createStudent.isPending}
        className="rounded-md bg-neutral-900 px-3 py-1.5 text-sm text-white disabled:opacity-50">
        {createStudent.isPending ? "Ekleniyor…" : "Öğrenci ekle"}
      </button>
      {error && <p className="w-full text-sm text-red-600">{error}</p>}
    </form>
  );
}
