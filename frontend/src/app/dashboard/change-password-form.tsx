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
    <form onSubmit={handleSubmit} className="grid gap-3 sm:grid-cols-3">
      <label className="form-label">Mevcut şifre
        <input
          type="password"
          required
          value={currentPassword}
          onChange={(e) => setCurrentPassword(e.target.value)}
          className="field text-sm"
          autoComplete="current-password"
        />
      </label>
      <label className="form-label">Yeni şifre (en az 8 karakter)
        <input
          type="password"
          required
          minLength={8}
          value={newPassword}
          onChange={(e) => setNewPassword(e.target.value)}
          className="field text-sm"
          autoComplete="new-password"
        />
      </label>
      <label className="form-label">Yeni şifre tekrarı
        <input
          type="password"
          required
          minLength={8}
          value={confirmPassword}
          onChange={(e) => setConfirmPassword(e.target.value)}
          className="field text-sm"
          autoComplete="new-password"
        />
      </label>
      <button type="submit" disabled={changePassword.isPending} className="btn btn-primary justify-self-start sm:col-span-3">
        {changePassword.isPending ? "Kaydediliyor…" : "Şifreyi değiştir"}
      </button>
      {error && <p role="alert" className="text-sm font-semibold text-[var(--danger-strong)] sm:col-span-3">{error}</p>}
    </form>
  );
}
