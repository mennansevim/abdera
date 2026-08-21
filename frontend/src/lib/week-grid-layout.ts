// Haftalık ızgara (dashboard önizlemesi + dashboard/calendar tam takvim) için ortak, saf
// yerleşim matematiği. İki ekran da aynı saat penceresi ve çakışma mantığını kullanır - kopyala
// yapıştır yerine burada tek merkez (bkz. docs/14-ui-design-prompt.md B3).

export interface GridLessonInput {
  id: string;
  startAt: string;
  endAt: string;
}

export interface HourWindow {
  startHour: number;
  endHour: number;
}

const DEFAULT_START_HOUR = 9;
const DEFAULT_END_HOUR = 19;

/**
 * Saat penceresini haftanın gerçek en erken/en geç dersinden türetir - sabit 09-19 yerine.
 * En erken dersin başladığı saatin başı, en geç dersin bittiği saatin bir sonraki tam saati.
 * Hiç ders yoksa 09:00-19:00 varsayılanına döner. Pencere her zaman en az 1 saat.
 */
export function computeHourWindow(lessons: GridLessonInput[]): HourWindow {
  if (!lessons.length) {
    return { startHour: DEFAULT_START_HOUR, endHour: DEFAULT_END_HOUR };
  }
  let minHour = 24;
  let maxHour = 0;
  for (const lesson of lessons) {
    const start = new Date(lesson.startAt);
    const end = new Date(lesson.endAt);
    minHour = Math.min(minHour, start.getHours());
    const endHour = end.getHours() + (end.getMinutes() > 0 ? 1 : 0);
    maxHour = Math.max(maxHour, endHour);
  }
  if (maxHour <= minHour) maxHour = minHour + 1;
  return { startHour: minHour, endHour: maxHour };
}

export interface LessonLayout {
  /** 0-1 aralığında, pencerenin üstünden yüzde konum */
  top: number;
  /** 0-1 aralığında, pencere yüksekliğine göre yüzde uzunluk */
  height: number;
  /** Çakışma grubu içindeki sütun index'i */
  column: number;
  /** Çakışma grubu içindeki toplam sütun sayısı */
  columns: number;
}

/**
 * Aynı gün içindeki dersleri saat penceresine göre konumlandırır ve zaman aralığı kesişen
 * dersleri yan yana sütunlara ayırır (klasik takvim çakışma algoritması: kümeleme + greedy
 * sütun atama). Önceki sürüm çakışan dersleri tam üst üste bindiriyordu - biri diğerini
 * tamamen gizliyordu.
 */
export function layoutDayLessons<T extends GridLessonInput>(
  dayLessons: T[],
  window: HourWindow,
): Map<string, LessonLayout> {
  const windowMinutes = (window.endHour - window.startHour) * 60;
  const result = new Map<string, LessonLayout>();
  if (!dayLessons.length || windowMinutes <= 0) return result;

  const sorted = [...dayLessons].sort((a, b) => a.startAt.localeCompare(b.startAt));

  type Entry = { lesson: T; startMin: number; endMin: number; column: number };
  let cluster: Entry[] = [];
  let clusterEnd = -Infinity;
  const columnEnds: number[] = [];

  function flushCluster() {
    if (!cluster.length) return;
    const totalColumns = Math.max(...cluster.map((entry) => entry.column)) + 1;
    for (const entry of cluster) {
      const startMinutes = new Date(entry.lesson.startAt).getHours() * 60 + new Date(entry.lesson.startAt).getMinutes() - window.startHour * 60;
      const durationMinutes = Math.max(15, entry.endMin - entry.startMin);
      const top = Math.max(0, Math.min(1, startMinutes / windowMinutes));
      const height = Math.max(0.02, Math.min(1 - top, durationMinutes / windowMinutes));
      result.set(entry.lesson.id, { top, height, column: entry.column, columns: totalColumns });
    }
    cluster = [];
    columnEnds.length = 0;
    clusterEnd = -Infinity;
  }

  for (const lesson of sorted) {
    const start = new Date(lesson.startAt);
    const end = new Date(lesson.endAt);
    const startMin = start.getHours() * 60 + start.getMinutes();
    const endMin = end.getHours() * 60 + end.getMinutes();

    if (cluster.length && startMin >= clusterEnd) {
      flushCluster();
    }

    let column = columnEnds.findIndex((end) => end <= startMin);
    if (column === -1) {
      column = columnEnds.length;
      columnEnds.push(endMin);
    } else {
      columnEnds[column] = endMin;
    }

    cluster.push({ lesson, startMin, endMin, column });
    clusterEnd = Math.max(clusterEnd, endMin);
  }
  flushCluster();

  return result;
}
