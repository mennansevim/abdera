// Hedef gelir planlayıcısı (deneysel) - Gelir ve gider ekranındaki "Hedef belirle" penceresi.
//
// Saf hesap: okulun kendi verisinden (bu ayın aidatları, tarifeler, öğretmenlerin enstrümanları
// ve ders başı ücretleri, giderler) bugünkü aylık geliri çıkarır, hedefe kalan açığı bir
// danışmanın sırasıyla kapatır: önce bedava kaldıraçlar (tahsilat, indirim disiplini), sonra
// mevcut öğretmenlerin boş kapasitesi (enstrüman bazında yeni öğrenci), sonra fiyat, en son
// yeni öğretmen / grup sınıfı. Hiçbir şey yazmaz, sunucuya gitmez; tutarların gerçek hesabı
// her zaman TuitionCalculator'dadır, buradaki rakamlar yalnızca tahmindir.
//
// Varsayımlar (kapasite, grup büyüklüğü, aylık kayıt temposu, azami zam) ekranda değiştirilebilir.

import type { BillingDue, CourseKind, TuitionRate } from "./billing";
import type { Instrument, Teacher } from "./people";

export interface PlanAssumptions {
  // Bir öğretmenin haftada kaldırabileceği ders saati (= birebir öğrenci sayısı).
  capacityPerTeacher: number;
  groupSize: number;
  // Gerçekçi aylık net yeni kayıt.
  monthlyNewEnrollments: number;
  maxPriceIncreasePercent: number;
  targetCollectionRate: number;
}

export interface PlanInput {
  dues: BillingDue[];
  currentMonth: string;
  rates: TuitionRate[];
  teachers: Teacher[];
  instruments: Instrument[];
  teacherRates: Record<string, number | null>;
  monthlyExpense: number;
  target: number;
  assumptions: PlanAssumptions;
}

export interface Baseline {
  period: string;
  enrollments: number;
  individual: number;
  group: number;
  runRate: number;
  collectionRate: number;
  expectedCash: number;
  discountTotal: number;
  overdue: number;
  overdueStudents: number;
  monthlyExpense: number;
  net: number;
}

export interface TeacherLoad { id: string; name: string; load: number; spare: number; ratePerLesson: number | null }

export interface InstrumentRow {
  id: string;
  name: string;
  individual: number;
  group: number;
  revenue: number;
  teachers: TeacherLoad[];
  spare: number;
  marginPerStudent: number;
}

export interface PlanStep {
  key: "collection" | "discount" | "capacity" | "price" | "growth";
  title: string;
  when: string;
  detail: string;
  impact: number;
  cost: number;
  items: { label: string; value: string }[];
}

export interface RevenuePlan {
  baseline: Baseline;
  instruments: InstrumentRow[];
  gap: number;
  steps: PlanStep[];
  projectedCash: number;
  projectedNet: number;
  remaining: number;
  reachMonth: string | null;
  notes: string[];
  sensitivity: { label: string; value: number }[];
}

export const DEFAULT_ASSUMPTIONS: PlanAssumptions = {
  capacityPerTeacher: 25,
  groupSize: 4,
  monthlyNewEnrollments: 6,
  maxPriceIncreasePercent: 10,
  targetCollectionRate: 0.97,
};

const MONTHS = ["Ocak", "Şubat", "Mart", "Nisan", "Mayıs", "Haziran", "Temmuz", "Ağustos", "Eylül", "Ekim", "Kasım", "Aralık"];
export const addMonths = (key: string, count: number) => {
  const total = Number(key.slice(0, 4)) * 12 + Number(key.slice(5, 7)) - 1 + count;
  return `${Math.floor(total / 12)}-${String((total % 12) + 1).padStart(2, "0")}`;
};
export const monthName = (key: string) => `${MONTHS[Number(key.slice(5, 7)) - 1]} ${key.slice(0, 4)}`;
const tl = (value: number) => `₺${Math.round(value).toLocaleString("tr-TR")}`;

// Zam yeni dönem başında yapılır: Eylül (eğitim yılı) veya Ocak, hangisi önce geliyorsa.
function nextTermStart(currentMonth: string) {
  const year = Number(currentMonth.slice(0, 4));
  const candidates = [`${year}-09`, `${year + 1}-01`, `${year + 1}-09`].filter((key) => key > currentMonth);
  return candidates[0];
}

function currentRate(rates: TuitionRate[], kind: CourseKind) {
  return rates.find((rate) => rate.courseKind === kind && rate.isCurrent) ?? rates.filter((rate) => rate.courseKind === kind).sort((a, b) => b.effectiveFrom.localeCompare(a.effectiveFrom))[0];
}

export function buildRevenuePlan(input: PlanInput): RevenuePlan {
  const { dues, currentMonth, rates, teachers, instruments, teacherRates, monthlyExpense, target, assumptions } = input;
  const live = dues.filter((due) => due.status !== "Cancelled");

  // Bu ayın aidatı henüz açılmadıysa en son açılmış dönem temel alınır.
  const periods = Array.from(new Set(live.map((due) => due.period))).filter((period) => period <= currentMonth).sort();
  const basePeriod = periods.includes(currentMonth) ? currentMonth : periods.at(-1) ?? currentMonth;
  const base = live.filter((due) => due.period === basePeriod);

  // Tahsilat oranı: tamamlanmış son üç dönem (içinde bulunulan ay hâlâ tahsil ediliyor).
  const closed = periods.filter((period) => period < basePeriod).slice(-3);
  const closedDues = live.filter((due) => closed.includes(due.period));
  const closedAmount = closedDues.reduce((sum, due) => sum + due.amount, 0);
  const collectionRate = closedAmount > 0
    ? Math.min(1, closedDues.reduce((sum, due) => sum + Math.min(due.totalPaid, due.amount), 0) / closedAmount)
    : 0.95;

  const runRate = base.reduce((sum, due) => sum + due.amount, 0);
  const expectedCash = runRate * collectionRate;
  const discountTotal = base.reduce((sum, due) => sum + Math.max(0, due.baseAmount - due.amount), 0);
  const overdueDues = live.filter((due) => due.period < basePeriod && due.status !== "Paid" && due.amount > due.totalPaid);
  const overdue = overdueDues.reduce((sum, due) => sum + (due.amount - due.totalPaid), 0);
  const baseline: Baseline = {
    period: basePeriod,
    enrollments: base.length,
    individual: base.filter((due) => due.courseKind === "Individual").length,
    group: base.filter((due) => due.courseKind === "Group").length,
    runRate,
    collectionRate,
    expectedCash,
    discountTotal,
    overdue,
    overdueStudents: new Set(overdueDues.map((due) => due.studentId)).size,
    monthlyExpense,
    net: expectedCash - monthlyExpense,
  };

  const individualRate = currentRate(rates, "Individual");
  const groupRate = currentRate(rates, "Group");
  const lessonsPerMonth = individualRate?.lessonsPerMonth ?? 4;
  const avgDiscount = runRate > 0 && runRate + discountTotal > 0 ? discountTotal / (runRate + discountTotal) : 0;
  const netIndividual = (individualRate?.monthlyAmount ?? (base.length ? runRate / base.length : 0)) * (1 - avgDiscount);
  const netGroup = (groupRate?.monthlyAmount ?? netIndividual / 2) * (1 - avgDiscount);
  const knownRates = Object.values(teacherRates).filter((rate): rate is number => rate !== null && rate > 0);
  const avgTeacherRate = knownRates.length ? knownRates.reduce((sum, rate) => sum + rate, 0) / knownRates.length : 0;
  const rateOf = (teacherId: string) => teacherRates[teacherId] ?? avgTeacherRate;

  // Öğretmen yükü: birebir kayıt bir saat, grup kaydı bir saatin grup büyüklüğünde biri.
  const loadByTeacher = new Map<string, number>();
  for (const due of base) loadByTeacher.set(due.teacherId, (loadByTeacher.get(due.teacherId) ?? 0) + (due.courseKind === "Group" ? 1 / assumptions.groupSize : 1));
  const activeTeachers = teachers.filter((teacher) => teacher.status === "Active");
  const teacherLoads = new Map<string, TeacherLoad>(activeTeachers.map((teacher) => {
    const load = loadByTeacher.get(teacher.id) ?? 0;
    return [teacher.id, { id: teacher.id, name: `${teacher.firstName} ${teacher.lastName}`, load, spare: Math.max(0, Math.floor(assumptions.capacityPerTeacher - load)), ratePerLesson: teacherRates[teacher.id] ?? null }];
  }));

  // Enstrüman → öğretmenler: öğretmen künyesindeki enstrümanlar + fiilen ders verdiği enstrümanlar.
  const instrumentTeachers = new Map<string, Set<string>>();
  const link = (instrumentId: string, teacherId: string) => {
    if (!teacherLoads.has(teacherId)) return;
    instrumentTeachers.set(instrumentId, (instrumentTeachers.get(instrumentId) ?? new Set()).add(teacherId));
  };
  for (const teacher of activeTeachers) for (const instrumentId of teacher.instrumentIds) link(instrumentId, teacher.id);
  for (const due of base) link(due.instrumentId, due.teacherId);

  const names = new Map(instruments.map((instrument) => [instrument.id, instrument.name]));
  for (const due of base) names.set(due.instrumentId, due.instrumentName);
  const rows: InstrumentRow[] = Array.from(new Set([...instrumentTeachers.keys(), ...base.map((due) => due.instrumentId)])).map((id) => {
    const own = base.filter((due) => due.instrumentId === id);
    const teacherIds = Array.from(instrumentTeachers.get(id) ?? []);
    const teacherRows = teacherIds.map((teacherId) => teacherLoads.get(teacherId)!).sort((a, b) => b.spare - a.spare);
    const cost = teacherIds.length ? teacherIds.reduce((sum, teacherId) => sum + rateOf(teacherId), 0) / teacherIds.length : avgTeacherRate;
    return {
      id,
      name: names.get(id) ?? "Enstrüman",
      individual: own.filter((due) => due.courseKind === "Individual").length,
      group: own.filter((due) => due.courseKind === "Group").length,
      revenue: own.reduce((sum, due) => sum + due.amount, 0),
      teachers: teacherRows,
      spare: teacherRows.reduce((sum, teacher) => sum + teacher.spare, 0),
      marginPerStudent: netIndividual - lessonsPerMonth * cost,
    };
  }).sort((a, b) => b.revenue - a.revenue);

  const targetCollection = Math.max(collectionRate, assumptions.targetCollectionRate);
  const cashPerStudent = netIndividual * targetCollection;
  const gap = target - expectedCash;
  let remaining = gap;
  const steps: PlanStep[] = [];
  let reachOffset = 0;
  let addedCost = 0;
  const notes: string[] = [];

  // 1) Tahsilat: kesilmiş aidatın tamamını almak bedava gelirdir.
  if (remaining > 0 && collectionRate < assumptions.targetCollectionRate && runRate > 0) {
    const impact = Math.min(remaining, runRate * (assumptions.targetCollectionRate - collectionRate));
    remaining -= impact;
    reachOffset = Math.max(reachOffset, 1);
    steps.push({
      key: "collection",
      title: "Tahsilatı sıkılaştır",
      when: `${monthName(addMonths(currentMonth, 0))}`,
      detail: `Son üç dönemde tahsilat oranı %${Math.round(collectionRate * 100)}. Hedef oran %${Math.round(assumptions.targetCollectionRate * 100)} - bunun için yeni öğrenci gerekmez: vade günü WhatsApp hatırlatması, havale için sanal IBAN, 2 ay gecikende veliyle birebir görüşme.`,
      impact,
      cost: 0,
      items: overdue > 0 ? [{ label: "Geçmiş dönemlerden bekleyen", value: `${tl(overdue)} · ${baseline.overdueStudents} öğrenci (tek seferlik)` }] : [],
    });
  }

  // 2) İndirim disiplini: indirimler aidatın %8'ini aşıyorsa yenilemede gözden geçir.
  if (remaining > 0 && runRate + discountTotal > 0 && discountTotal / (runRate + discountTotal) > 0.08) {
    const impact = Math.min(remaining, discountTotal * 0.3 * targetCollection);
    remaining -= impact;
    reachOffset = Math.max(reachOffset, 2);
    const discounted = base.filter((due) => due.discountPercent > 0);
    const reasons = new Map<string, number>();
    for (const due of discounted) reasons.set(due.discountReason ?? "Diğer", (reasons.get(due.discountReason ?? "Diğer") ?? 0) + (due.baseAmount - due.amount));
    steps.push({
      key: "discount",
      title: "İndirimleri gözden geçir",
      when: `${monthName(currentMonth)} – ${monthName(addMonths(currentMonth, 1))}`,
      detail: `Bu ay ${discounted.length} kayıtta toplam ${tl(discountTotal)} indirim var (liste fiyatına göre %${Math.round((discountTotal / (runRate + discountTotal)) * 100)}). Elle verilen indirimleri süreli yap ve yenilemede üçte birini geri al; kardeş/çoklu kurs indirimleri politikada kalsın.`,
      impact,
      cost: 0,
      items: Array.from(reasons.entries()).sort((a, b) => b[1] - a[1]).slice(0, 4).map(([label, value]) => ({ label, value: `${tl(value)}/ay` })),
    });
  }

  // 3) Boş kapasite: her yeni öğrenci, talebi yüksek ve kâr payı iyi olan enstrümana; aynı
  //    enstrümana yığılınca getirisi azalır (pazar doygunluğu).
  if (remaining > 0 && cashPerStudent > 0) {
    const spare = new Map(Array.from(teacherLoads.values()).map((teacher) => [teacher.id, teacher.spare]));
    const added = new Map<string, Map<string, number>>();
    let count = 0;
    let impact = 0;
    let cost = 0;
    while (remaining - impact > 0 && count < 500) {
      let best: { row: InstrumentRow; teacherId: string; score: number } | null = null;
      for (const row of rows) {
        const teacherId = row.teachers.map((teacher) => teacher.id).filter((id) => (spare.get(id) ?? 0) > 0).sort((a, b) => (spare.get(b) ?? 0) - (spare.get(a) ?? 0))[0];
        if (!teacherId) continue;
        const demand = row.individual + row.group + 1;
        const already = Array.from(added.get(row.id)?.values() ?? []).reduce((sum, value) => sum + value, 0);
        const score = Math.max(1, row.marginPerStudent) * demand / (1 + (3 * already) / demand);
        if (!best || score > best.score) best = { row, teacherId, score };
      }
      if (!best) break;
      spare.set(best.teacherId, (spare.get(best.teacherId) ?? 0) - 1);
      const perTeacher = added.get(best.row.id) ?? new Map<string, number>();
      perTeacher.set(best.teacherId, (perTeacher.get(best.teacherId) ?? 0) + 1);
      added.set(best.row.id, perTeacher);
      count += 1;
      impact += cashPerStudent;
      cost += lessonsPerMonth * rateOf(best.teacherId);
    }
    if (count > 0) {
      remaining -= impact;
      addedCost += cost;
      const months = Math.ceil(count / Math.max(1, assumptions.monthlyNewEnrollments));
      const start = reachOffset;
      reachOffset += months;
      steps.push({
        key: "capacity",
        title: `Boş saatlere ${count} yeni birebir öğrenci`,
        when: `${monthName(addMonths(currentMonth, start))} – ${monthName(addMonths(currentMonth, reachOffset))}`,
        detail: `Mevcut öğretmenlerin boş saatleri dolduruluyor; öncelik çok talep gören ve ders başı maliyeti düşük enstrümanlarda. Ayda ${assumptions.monthlyNewEnrollments} kayıt temposuyla ${months} ay. Kanallar: mevcut velilerden tavsiye (kardeş/arkadaş), deneme dersi, dönem sonu konseri.`,
        impact,
        cost,
        items: rows.filter((row) => added.has(row.id)).map((row) => {
          const perTeacher = Array.from(added.get(row.id)!.entries());
          const total = perTeacher.reduce((sum, [, value]) => sum + value, 0);
          return {
            label: `${row.name} +${total}`,
            value: perTeacher.map(([teacherId, value]) => `${teacherLoads.get(teacherId)?.name ?? "Öğretmen"} ${value}`).join(", "),
          };
        }).sort((a, b) => Number(b.label.split("+")[1]) - Number(a.label.split("+")[1])),
      });
    }
  }

  // 4) Fiyat: kalan açığı kapatan zam oranı; azami oranın üstüne çıkılmaz.
  const billedAfter = runRate + (steps.find((step) => step.key === "capacity")?.impact ?? 0) / Math.max(targetCollection, 0.01);
  if (remaining > 0 && billedAfter > 0 && assumptions.maxPriceIncreasePercent > 0) {
    const needed = Math.ceil((remaining / (billedAfter * targetCollection)) * 100);
    const percent = Math.min(assumptions.maxPriceIncreasePercent, needed);
    const impact = Math.min(remaining, billedAfter * targetCollection * (percent / 100));
    remaining -= impact;
    const term = nextTermStart(currentMonth);
    const termOffset = (Number(term.slice(0, 4)) - Number(currentMonth.slice(0, 4))) * 12 + Number(term.slice(5, 7)) - Number(currentMonth.slice(5, 7));
    reachOffset = Math.max(reachOffset, termOffset);
    steps.push({
      key: "price",
      title: `${monthName(term)} döneminde %${percent} tarife güncellemesi`,
      when: monthName(term),
      detail: `${needed > percent ? `Açığı tek başına kapatmak %${needed} zam isterdi; kayıp riskini sınırlamak için %${percent} ile sınırlı tutuldu. ` : ""}Zammı dönem başında ve en az bir ay önceden duyur; peşin ödeyene eski fiyatı kilitleme fırsatı ver (peşin kampanyası). Geçmiş aidatlar değişmez.`,
      impact,
      cost: 0,
      items: [
        ...(individualRate ? [{ label: "Birebir", value: `${tl(individualRate.monthlyAmount)} → ${tl(individualRate.monthlyAmount * (1 + percent / 100))}` }] : []),
        ...(groupRate ? [{ label: "Grup", value: `${tl(groupRate.monthlyAmount)} → ${tl(groupRate.monthlyAmount * (1 + percent / 100))}` }] : []),
      ],
    });
  }

  // 5) Büyüme: kapasite bitti; en çok talep gören enstrümanlara yeni öğretmen ya da grup sınıfı.
  if (remaining > 0 && cashPerStudent > 0) {
    const students = Math.ceil(remaining / cashPerStudent);
    const perTeacher = Math.max(1, Math.floor(assumptions.capacityPerTeacher * 0.8));
    const newTeachers = Math.ceil(students / perTeacher);
    const leaders = rows.filter((row) => row.individual + row.group > 0).slice(0, 3);
    const groupCashPerSlot = netGroup * assumptions.groupSize * targetCollection;
    const groupSlots = groupCashPerSlot > 0 ? Math.ceil(remaining / groupCashPerSlot) : 0;
    const cost = students * lessonsPerMonth * avgTeacherRate;
    const months = 2 + Math.ceil(students / Math.max(1, assumptions.monthlyNewEnrollments));
    const start = reachOffset;
    reachOffset += months;
    addedCost += cost;
    steps.push({
      key: "growth",
      title: `${newTeachers} yeni öğretmen veya ${groupSlots} grup sınıfı`,
      when: `${monthName(addMonths(currentMonth, start))} – ${monthName(addMonths(currentMonth, reachOffset))}`,
      detail: `Mevcut kadro dolu. Kalan ${tl(remaining)} için yaklaşık ${students} öğrenci daha gerekiyor. İki yol: (a) ${newTeachers} yarı zamanlı öğretmen (işe alım ~2 ay), (b) mevcut öğretmenlerin akşam/hafta sonu saatlerine ${assumptions.groupSize} kişilik ${groupSlots} grup sınıfı - grup bir öğretmen saatinden ${tl(groupCashPerSlot)} getirir, birebir ${tl(cashPerStudent)}.`,
      impact: remaining,
      cost,
      items: leaders.map((row) => ({ label: row.name, value: `bugün ${row.individual + row.group} öğrenci · ${row.teachers.length} öğretmen` })),
    });
    remaining = 0;
  }

  // Danışman notları: veriden okunan, plandan bağımsız gözlemler.
  for (const teacher of teacherLoads.values()) {
    if (teacher.load > assumptions.capacityPerTeacher) notes.push(`${teacher.name} kapasitenin üstünde (${Math.round(teacher.load)}/${assumptions.capacityPerTeacher} saat). Yeni kayıtları aynı enstrümandaki başka öğretmene yönlendir.`);
  }
  for (const row of rows) {
    if (row.teachers.length > 0 && row.individual + row.group <= 2 && row.spare >= 5) notes.push(`${row.name}: öğretmen var ama ${row.individual + row.group} öğrenci. Deneme dersi ve okul içi tanıtımla en ucuz büyüme burada (${row.spare} boş saat).`);
  }
  if (groupRate && individualRate && baseline.group / Math.max(1, baseline.enrollments) < 0.15 && netGroup * assumptions.groupSize > netIndividual) {
    notes.push(`Grup dersi payı düşük (kayıtların %${Math.round((baseline.group / Math.max(1, baseline.enrollments)) * 100)}). ${assumptions.groupSize} kişilik bir grup, aynı öğretmen saatinden birebirin ${(netGroup * assumptions.groupSize / netIndividual).toLocaleString("tr-TR", { maximumFractionDigits: 1 })} katı gelir getirir.`);
  }
  if (monthlyExpense > expectedCash) notes.push(`Aylık gider (${tl(monthlyExpense)}) beklenen tahsilatı (${tl(expectedCash)}) aşıyor; büyümeden önce sabit giderleri gözden geçir.`);
  if (avgTeacherRate === 0) notes.push("Öğretmen ders başı ücretleri girilmemiş; kâr payı tahmini ücretsiz varsayıldı. Öğretmenler ekranından ücretleri gir.");

  const projectedCash = gap > 0 ? target : expectedCash;
  const sensitivity = [
    { label: "+1 birebir öğrenci", value: cashPerStudent },
    { label: `+1 grup öğrencisi`, value: netGroup * targetCollection },
    { label: "%5 tarife artışı", value: runRate * 0.05 * targetCollection },
    { label: "Tahsilatta +1 puan", value: runRate * 0.01 },
  ];

  return {
    baseline,
    instruments: rows,
    gap,
    steps,
    projectedCash,
    projectedNet: projectedCash - monthlyExpense - addedCost,
    remaining: Math.max(0, remaining),
    reachMonth: gap > 0 ? addMonths(currentMonth, Math.max(1, reachOffset)) : null,
    notes,
    sensitivity,
  };
}
