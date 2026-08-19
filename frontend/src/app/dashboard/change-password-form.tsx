"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useChangePassword } from "@/lib/use-auth";

export function ChangePasswordForm({ onDone }: { onDone: () => void }) {
  const changePassword = useChangePassword();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    try {
      await changePassword.mutateAsync({ currentPassword, newPassword });
      onDone();
    } catch (err) {
      setError(err instanceof ApiError ? err.detail ?? err.title : "Şifre değiştirilemedi.");
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mt-3 flex flex-wrap items-end gap-3">
      <div className="space-y-1">
        <label className="text-xs font-medium text-amber-900">Geçici şifre</label>
        <input
          type="password"
          required
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          className="rounded-md border border-amber-300 bg-white px-2 py-1 text-sm"
        />
      </div>
      <div className="space-y-1">
        <label className="text-xs font-medium text-amber-900">Yeni şifre (en az 8 karakter)</label>
        <input
          type="password"
          required
          minLength={8}
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          className="rounded-md border border-amber-300 bg-white px-2 py-1 text-sm"
        />
      </div>
      <button
        type="submit"
        disabled={changePassword.isPending}
        className="rounded-md bg-amber-900 px-3 py-1.5 text-sm font-medium text-white disabled:opacity-50"
      >
        {changePassword.isPending ? "Kaydediliyor…" : "Şifreyi değiştir"}
      </button>
      {error && <p className="w-full text-sm text-red-700">{error}</p>}
    </form>
  );
}
