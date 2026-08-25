"use client";

import { useState, type FormEvent } from "react";
import { ApiError } from "@/lib/api";
import { useChangePassword } from "@/lib/use-auth";

export function ChangePasswordForm({ onDone }: { onDone: () => void }) {
  const changePassword = useChangePassword();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (newPassword.length < 8) {
      setError("Yeni şifre en az 8 karakter olmalı.");
      return;
    }
    if (newPassword !== confirmPassword) {
      setError("Yeni şifre tekrarı eşleşmiyor.");
      return;
    }
    try {
      await changePassword.mutateAsync({ currentPassword, newPassword });
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
      onDone();
    } catch (err) {
      if (err instanceof ApiError) {
        const fieldError = Object.values(err.errors ?? {}).flat()[0];
        setError(fieldError ?? err.detail ?? err.title);
      } else {
        setError("Şifre değiştirilemedi.");
      }
    }
  }

  return (
    <form onSubmit={handleSubmit} className="mt-3 flex flex-wrap items-end gap-3">
      <div className="space-y-1.5">
        <label className="text-[.7rem] font-bold text-[var(--warning-strong)]">Mevcut şifre</label>
        <input
          type="password"
          required
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          className="field min-h-11 w-40 border-[var(--warning)]/50 text-sm"
          autoComplete="current-password"
        />
      </div>
      <div className="space-y-1.5">
        <label className="text-[.7rem] font-bold text-[var(--warning-strong)]">Yeni şifre (en az 8 karakter)</label>
        <input
          type="password"
          required
          minLength={8}
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          className="field min-h-11 w-48 border-[var(--warning)]/50 text-sm"
          autoComplete="new-password"
        />
      </div>
      <div className="space-y-1.5">
        <label className="text-[.7rem] font-bold text-[var(--warning-strong)]">Yeni şifre tekrarı</label>
        <input
          type="password"
          required
          minLength={8}
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          className="field min-h-11 w-48 border-[var(--warning)]/50 text-sm"
          autoComplete="new-password"
        />
      </div>
      <button
        type="submit"
        disabled={changePassword.isPending}
        className="pressable min-h-11 rounded-xl bg-[var(--warning-strong)] px-4 text-sm font-bold text-white disabled:opacity-50"
      >
        {changePassword.isPending ? "Kaydediliyor…" : "Şifreyi değiştir"}
      </button>
      {error && <p className="w-full text-sm font-medium text-[var(--danger-strong)]">{error}</p>}
    </form>
  );
}
