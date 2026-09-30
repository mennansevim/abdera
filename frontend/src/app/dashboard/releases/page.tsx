"use client";

import { useEffect } from "react";
import { PageHeader } from "@/components/ui";
import { ReleaseNotes } from "@/components/whats-new";
import { CURRENT_VERSION, markReleaseSeen, releasesFor } from "@/lib/releases";
import { useMe } from "@/lib/use-auth";

export default function ReleasesPage() {
  const { data: me } = useMe();

  // Notları buradan okuyan kullanıcıya aynı sürüm için ayrıca pencere açılmasın.
  useEffect(() => {
    if (me) markReleaseSeen(me.id);
  }, [me]);

  if (!me) return null;

  return (
    <div className="mx-auto max-w-3xl space-y-3">
      <PageHeader title="Yenilikler" description={`Her güncellemede nelerin değiştiği. Şu an kullandığın sürüm: ${CURRENT_VERSION}.`} />
      <section className="app-card divide-y divide-[var(--line)]">
        {releasesFor(me.role).map((release) => (
          <div key={release.version} className="p-4 sm:p-5">
            <ReleaseNotes release={release} isCurrent={release.version === CURRENT_VERSION} />
          </div>
        ))}
      </section>
    </div>
  );
}
