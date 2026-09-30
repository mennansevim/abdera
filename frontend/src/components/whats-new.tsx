"use client";

import Link from "next/link";
import { useEffect, useState } from "react";
import { Modal } from "@/components/ui";
import type { Release } from "@/data/releases";
import type { Me } from "@/lib/api";
import { CURRENT_VERSION, formatReleaseDate, markReleaseSeen, readSeenRelease, unseenReleases } from "@/lib/releases";

export function ReleaseNotes({ release, isCurrent = false }: { release: Release; isCurrent?: boolean }) {
  return (
    <article>
      <header className="flex flex-wrap items-baseline gap-x-2 gap-y-1">
        <h3 className="text-sm font-bold">Sürüm {release.version}</h3>
        <span className="text-meta">{formatReleaseDate(release.date)}</span>
        {isCurrent && <span className="rounded-full bg-[var(--brand-soft)] px-2 py-0.5 text-[.75rem] font-bold text-[var(--brand-strong)]">Kullandığın sürüm</span>}
      </header>
      {release.items.length > 0
        ? (
          <ul className="mt-2 list-disc space-y-1.5 pl-5 text-sm leading-relaxed">
            {release.items.map((item) => <li key={item.text}>{item.text}</li>)}
          </ul>
        )
        : <p className="text-meta mt-2">Ekranda görünen bir değişiklik yok; arka plan iyileştirmeleri.</p>}
    </article>
  );
}

// Canlıya yeni bir sürüm çıktıktan sonraki ilk açılışta, kullanıcının henüz görmediği sürüm
// notlarını bir kez gösterir. Zorunlu şifre değişikliği ekranında açılmaz - önce o iş bitsin.
export function WhatsNewDialog({ me }: { me: Me }) {
  const [releases, setReleases] = useState<Release[]>([]);

  useEffect(() => {
    // Otomasyonla sürülen tarayıcıda (Playwright e2e) her test temiz depoyla açılır; pencere
    // her sayfanın önüne geçip tıklamaları kapatırdı.
    if (me.mustChangePassword || navigator.webdriver) return;
    // localStorage yalnızca istemcide okunabilir; ilk HTML sunucuyla aynı kalsın diye
    // karar mount sonrasında verilir.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setReleases(unseenReleases(me.role, readSeenRelease(me.id)));
  }, [me.id, me.role, me.mustChangePassword]);

  function close() {
    markReleaseSeen(me.id);
    setReleases([]);
  }

  return (
    <Modal open={releases.length > 0} title="Yenilikler" description="Abdera güncellendi. Bu sürümde değişenler:" onClose={close} size="sm">
      <div className="space-y-5">
        {releases.map((release) => <ReleaseNotes key={release.version} release={release} isCurrent={release.version === CURRENT_VERSION} />)}
      </div>
      <div className="mt-5 flex items-center justify-between gap-3 border-t border-[var(--line)] pt-4">
        <Link href="/dashboard/releases" onClick={close} className="text-sm font-semibold text-[var(--brand)] hover:underline">Tüm sürüm notları</Link>
        <button type="button" onClick={close} className="btn btn-primary min-h-11 px-5 text-sm">Tamam</button>
      </div>
    </Modal>
  );
}
