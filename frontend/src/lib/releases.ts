import { RELEASES, type Release } from "@/data/releases";
import type { UserRole } from "@/lib/api";

export const CURRENT_VERSION = RELEASES[0].version;

// Rolün göremeyeceği maddeler ayıklanır; sürümün kendisi (boş kalsa bile) listede kalır.
export function releasesFor(role: UserRole): Release[] {
  return RELEASES.map((release) => ({
    ...release,
    items: release.items.filter((item) => item.audience === "all" || role === "Admin"),
  }));
}

// Kullanıcının son gördüğü sürümden sonra gelen, ona görünen maddesi olan sürümler.
// Hiç kayıt yoksa (ilk giriş, yeni cihaz) ya da kayıtlı sürüm listede yoksa yalnızca en son
// maddeli sürüm gösterilir - geçmişin tamamı ilk açılışta yığılmasın.
export function unseenReleases(role: UserRole, lastSeenVersion: string | null): Release[] {
  const visible = releasesFor(role);
  const seenIndex = lastSeenVersion === null ? -1 : visible.findIndex((release) => release.version === lastSeenVersion);
  const withItems = (seenIndex === -1 ? visible : visible.slice(0, seenIndex)).filter((release) => release.items.length > 0);
  return seenIndex === -1 ? withItems.slice(0, 1) : withItems;
}

// "Görüldü" bilgisi cihaz başına tutulur: kaybolursa en kötü ihtimalle son sürüm notu bir kez
// daha açılır. Aynı tarayıcıyı paylaşan iki kullanıcı birbirinin kaydını ezmesin diye anahtar
// kullanıcıya özeldir.
function storageKey(userId: string) {
  return `abdera:seen-release:${userId}`;
}

export function readSeenRelease(userId: string): string | null {
  try {
    return window.localStorage.getItem(storageKey(userId));
  } catch {
    return null;
  }
}

export function markReleaseSeen(userId: string) {
  try {
    window.localStorage.setItem(storageKey(userId), CURRENT_VERSION);
  } catch {
    // Gizli mod/depolama kapalı: pencere bir sonraki açılışta yeniden görünür, iş bozulmaz.
  }
}

export function formatReleaseDate(date: string) {
  return new Date(`${date}T12:00:00`).toLocaleDateString("tr-TR", { day: "numeric", month: "long", year: "numeric" });
}
