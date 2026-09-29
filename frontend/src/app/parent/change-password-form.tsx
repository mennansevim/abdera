"use client";

import { useState, type FormEvent } from "react";
import { FormActions, FormMessage } from "@/components/ui";
import { ApiError } from "@/lib/api";
import { useChangeGuardianPassword } from "@/lib/guardian-auth";

const MIN_LENGTH = 6;

// docs/10-decisions.md Q1: veli ilk şifresi ad soyaddan türer (ör. Mennan Sevim → sevimm);
// veli bu formdan kendi şifresini belirler. Kural sunucuda da denetlenir, burada yalnızca
// erken ve anlaşılır uyarı verilir.
export function GuardianChangePasswordForm({ onClose, onDone }: { onClose: () => void; onDone: () => void }) {
  const changePassword = useChangeGuardianPassword();
  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function handleSubmit(event: FormEvent) {
    event.preventDefault();
    setError(null);
    if (newPassword.length < MIN_LENGTH) {
      setError(`Yeni şifre en az ${MIN_LENGTH} karakter olmalı.`);
      return;
    }
    if (newPassword !== confirmPassword) {
      setError("Yeni şifre tekrarı eşleşmiyor.");
      return;
    }
    try {
      await changePassword.mutateAsync({ currentPassword, newPassword });
      onDone();
    } catch (err) {
      if (err instanceof ApiError) {
        setError(Object.values(err.errors ?? {}).flat()[0] ?? err.detail ?? err.title);
      } else {
        setError("Şifre değiştirilemedi.");
      }
    }
  }

  return (
    <form onSubmit={handleSubmit} className="space-y-3">
      <label className="form-label">Mevcut şifre
        <input type="password" required autoFocus value={currentPassword} onChange={(e) => setCurrentPassword(e.target.value)} className="field text-sm" autoComplete="current-password" />
      </label>
      <label className="form-label">Yeni şifre (en az {MIN_LENGTH} karakter)
        <input type="password" required minLength={MIN_LENGTH} value={newPassword} onChange={(e) => setNewPassword(e.target.value)} className="field text-sm" autoComplete="new-password" />
      </label>
      <label className="form-label">Yeni şifre tekrarı
        <input type="password" required minLength={MIN_LENGTH} value={confirmPassword} onChange={(e) => setConfirmPassword(e.target.value)} className="field text-sm" autoComplete="new-password" />
      </label>
      {error && <FormMessage tone="error">{error}</FormMessage>}
      <FormActions onCancel={onClose} submitLabel="Şifreyi değiştir" pending={changePassword.isPending} />
    </form>
  );
}
