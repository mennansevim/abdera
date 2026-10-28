"use client";

import Link from "next/link";
import { useParams } from "next/navigation";
import { useState } from "react";
import { Icon } from "@/components/icons";
import { SchoolAvatar, StatusBadge } from "@/components/school-bits";
import { Badge, ConfirmDialog, FormMessage, LoadError, Modal, Notice, Panel, Skeleton, Toggle } from "@/components/ui";
import { errorMessage } from "@/lib/api";
import { compareVersions, useReleases } from "@/lib/releases";
import {
  isRunOpen,
  useSchool,
  useSchoolAction,
  useSchoolFlags,
  useUpgradeSchool,
  type SchoolAction,
  type SchoolDetail,
} from "@/lib/schools";
import { AlertsTab, AuditTab, GeneralTab, ModulesTab, RunsTab, TelemetryTab } from "./tabs";
import { DomainTab } from "./domain-tab";
import { PackageTab } from "./package-tab";
import { BackupsTab, LogsTab } from "./monitoring-tabs";

type TabKey = "general" | "runs" | "modules" | "package" | "domain" | "telemetry" | "logs" | "backups" | "alerts" | "audit";

const TABS: { key: TabKey; label: string }[] = [
  { key: "general", label: "Genel" },
  { key: "runs", label: "İşlemler" },
  { key: "modules", label: "Modüller" },
  { key: "package", label: "Paket" },
  { key: "domain", label: "Alan adı" },
  { key: "telemetry", label: "Telemetri" },
  { key: "logs", label: "Loglar" },
  { key: "backups", label: "Yedekler" },
  { key: "alerts", label: "Alarmlar" },
  { key: "audit", label: "Denetim" },
];

// Her okul işleminin onay metni. Yıkıcı olanlar kırmızı düğmeyle, silme ve kalıcı silme okulun
// kısaltması yazılarak onaylanır (§4.3 "Tehlikeli bölge", §16.9).
const ACTIONS: Record<SchoolAction, { label: string; title: string; description: string; danger?: boolean; typeSlug?: boolean }> = {
  suspend: {
    label: "Askıya al",
    title: "Okulu askıya al",
    description: "Önce son yedek alınır (başarısızsa işlem durur), sonra okul kapatılır. Veri silinmez; “Devam ettir” ile geri açılır.",
  },
  resume: { label: "Devam ettir", title: "Okulu devam ettir", description: "Askıdaki okul yeniden başlatılır." },
  destroy: {
    label: "Sil",
    title: "Okulu sil",
    description: "Okul durdurulur ve “Silinmeyi bekliyor” durumuna geçer. Veri 30 gün sunucuda kalır; bu sürede “Silmeyi geri al” ile kurtarılabilir. Kalıcı silme ancak sunucu ajanının tuttuğu 30 günlük süre dolunca yapılabilir; platform bu süreyi kısaltamaz.",
    danger: true,
    typeSlug: true,
  },
  undestroy: { label: "Silmeyi geri al", title: "Silmeyi geri al", description: "Bekleyen silme iptal edilir ve okul yeniden başlatılır." },
  purge: {
    label: "Kalıcı sil",
    title: "Okulu kalıcı sil",
    description: "Okulun sunucudaki verisi, DNS kaydı ve ayrılan kapasite kaldırılır. Bu geri alınamaz. 30 günlük bekleme dolmadıysa ajan reddeder. Yedekler saklama kuralına göre ayrıca durur.",
    danger: true,
    typeSlug: true,
  },
  retry: { label: "Yeniden dene", title: "Son işlemi yeniden dene", description: "Başarısız işlem kaldığı adımdan devam eder." },
  cancel: {
    label: "İptal et ve temizle",
    title: "Kurulumu iptal et",
    description: "Yarım kalan kurulumun yönlendirmesi, container'ları, DNS kaydı ve kapasitesi temizlenir; okul “Silindi” olur.",
    danger: true,
    typeSlug: true,
  },
  drill: {
    label: "Prova geri yükleme",
    title: "Geri yükleme provası",
    description: "Okulun anlık dökümü alınıp aynı sunucuda yayına açılmayan bir prova kopyasına geri yüklenir. Prova kopyasında WhatsApp/e-posta/banka kapalıdır, bekleyen bildirimler gönderilmez.",
  },
};

function availableActions(detail: SchoolDetail): SchoolAction[] {
  const status = detail.school.status;
  const latest = detail.runs[0];
  const actions: SchoolAction[] = [];
  if (latest?.status === "Failed") actions.push("retry");
  if (status === "Failed") actions.push("cancel");
  if (status === "Active") actions.push("suspend", "drill", "destroy");
  if (status === "Suspended") actions.push("resume", "drill", "destroy");
  if (status === "PendingDestroy") actions.push("undestroy", "purge");
  return actions;
}

// Okul detayı: üstte kimlik + durum + eylemler, altında sekmeler (§4.3).
export default function SchoolDetailPage() {
  const params = useParams<{ id: string }>();
  const id = params.id;
  const { data, isLoading, error, refetch } = useSchool(id);
  const [tab, setTab] = useState<TabKey>("general");
  const [confirming, setConfirming] = useState<SchoolAction | null>(null);
  const [upgradeOpen, setUpgradeOpen] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const action = useSchoolAction(id);
  const flags = useSchoolFlags(id);

  if (isLoading) return <div className="grid gap-4"><Skeleton className="h-28" /><Skeleton className="h-64" /></div>;
  if (error || !data) return <LoadError message={error ? errorMessage(error) : "Okul bulunamadı."} onRetry={() => refetch()} />;

  const school = data.school;
  const openRun = data.runs.some((run) => isRunOpen(run.status));
  const actions = availableActions(data);
  const dangerActions = actions.filter((kind) => ACTIONS[kind].danger);
  const config = confirming ? ACTIONS[confirming] : null;

  function runAction(kind: SchoolAction) {
    action.mutate(kind, {
      onSuccess: () => {
        setConfirming(null);
        setNotice(`${ACTIONS[kind].label} sıraya alındı. İlerlemeyi İşlemler sekmesinden izleyebilirsin.`);
        setTab("runs");
      },
    });
  }

  return (
    <div className="grid gap-5">
      <div>
        <Link href="/" className="text-meta inline-flex items-center gap-1 hover:text-[var(--brand)]"><Icon name="arrow-left" className="h-3.5 w-3.5" />Okullar</Link>
      </div>

      <header className="app-card grid gap-0 overflow-hidden">
        <div className="flex flex-wrap items-start gap-4 p-4 sm:p-5 pb-3">
          <SchoolAvatar name={school.shortName || school.displayName} color={school.primaryColor} size="lg" />
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="text-display">{school.displayName}</h1>
              <StatusBadge status={school.status} />
              {school.isCanary && <Badge tone="warning">Kanarya</Badge>}
              {school.isVersionPinned && <Badge tone="info">Sürüm sabit</Badge>}
            </div>
            <p className="text-meta mt-1 flex flex-wrap items-center gap-x-3 gap-y-1">
              <a href={`https://${school.publicDomain ?? school.primaryDomain}`} target="_blank" rel="noreferrer" className="inline-flex items-center gap-1 font-mono hover:text-[var(--brand)]">{school.publicDomain ?? school.primaryDomain}<Icon name="external" className="h-3 w-3" /></a>
              <span className="font-mono">{school.appVersion ? `v${school.appVersion}` : "kurulmadı"}{school.targetVersion && school.targetVersion !== school.appVersion ? ` → v${school.targetVersion}` : ""}</span>
              <span className="inline-flex items-center gap-1"><Icon name="server" className="h-3.5 w-3.5" />{school.hostName ?? "sunucu atanmadı"}</span>
            </p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2 px-4 pb-4 sm:px-5">
          {school.status === "Active" && (
            <button type="button" className="btn btn-primary" disabled={openRun} onClick={() => setUpgradeOpen(true)}>
              <Icon name="rocket" className="h-4 w-4" />Güncelle
            </button>
          )}
          {actions.filter((kind) => !ACTIONS[kind].danger).map((kind) => (
            <button
              key={kind}
              type="button"
              disabled={openRun && kind !== "retry"}
              onClick={() => { action.reset(); setConfirming(kind); }}
              className="btn btn-quiet"
            >
              {ACTIONS[kind].label}
            </button>
          ))}
        </div>
        {openRun && <p className="text-meta px-4 pb-3 sm:px-5">Süren bir işlem var; bitene kadar yeni işlem başlatılamaz.</p>}
        {flags.error && <div className="px-4 pb-3 sm:px-5"><FormMessage tone="error">{errorMessage(flags.error)}</FormMessage></div>}
      <div role="tablist" aria-label="Okul sekmeleri" className="flex gap-1 overflow-x-auto border-t border-[var(--line)] px-3">
        {TABS.map((item) => (
          <button
            key={item.key}
            type="button"
            role="tab"
            aria-selected={tab === item.key}
            onClick={() => setTab(item.key)}
            className={`-mb-px shrink-0 border-b-2 px-3 py-2 text-sm font-bold ${tab === item.key ? "border-[var(--brand)] text-[var(--brand-strong)]" : "border-transparent text-[var(--muted)] hover:text-[var(--foreground)]"}`}
          >
            {item.label}
            {item.key === "alerts" && school.openAlerts > 0 && <span className="ml-1.5 rounded-full bg-[var(--danger-soft)] px-1.5 text-[.7rem] text-[var(--danger-strong)]">{school.openAlerts}</span>}
            {item.key === "runs" && openRun && <span className="pulse-dot ml-1.5 inline-block h-2 w-2 rounded-full bg-[var(--info)]" aria-label="süren işlem" />}
          </button>
        ))}
      </div>

      </header>

      {notice && <Notice onDismiss={() => setNotice(null)}>{notice}</Notice>}

      <div role="tabpanel">
        {tab === "general" && (
          <GeneralTab
            detail={data}
            release={
              <Panel title="Yayın ayarları" flush>
                <div className="grid gap-0 px-4 py-1 text-sm">
                  <label className="flex items-center justify-between gap-4 border-b border-[var(--line)] py-2.5">
                    <span className="text-[var(--muted)]">Kanarya kanalı</span>
                    <Toggle checked={school.isCanary} disabled={flags.isPending} label="Kanarya" onChange={(value) => flags.mutate({ isCanary: value, isVersionPinned: school.isVersionPinned })} />
                  </label>
                  <label className="flex items-center justify-between gap-4 py-2.5" title="Sabit sürümlü okul filo güncellemelerine katılmaz.">
                    <span className="text-[var(--muted)]">Sürümü sabitle</span>
                    <Toggle checked={school.isVersionPinned} disabled={flags.isPending} label="Sürümü sabitle" onChange={(value) => flags.mutate({ isCanary: school.isCanary, isVersionPinned: value })} />
                  </label>
                </div>
              </Panel>
            }
          />
        )}
        {tab === "runs" && <RunsTab detail={data} />}
        {tab === "modules" && <ModulesTab detail={data} onQueued={() => { setNotice("Yapılandırma sıraya alındı; okul ~30 sn yeniden başlayacak."); setTab("runs"); }} />}
        {tab === "package" && <PackageTab detail={data} onQueued={(message) => { setNotice(message); if (message.includes("yeniden başlayacak")) setTab("runs"); }} />}
        {tab === "domain" && <DomainTab detail={data} onQueued={(message) => { setNotice(message); setTab("runs"); }} />}
        {tab === "telemetry" && <TelemetryTab detail={data} />}
        {tab === "logs" && <LogsTab detail={data} />}
        {tab === "backups" && <BackupsTab detail={data} />}
        {tab === "alerts" && <AlertsTab detail={data} />}
        {tab === "audit" && <AuditTab schoolId={id} />}
      </div>

      {dangerActions.length > 0 && (
        <section className="flex flex-wrap items-center gap-4 rounded-md border border-[var(--danger)]/50 bg-[var(--surface)] px-5 py-4">
          <div className="min-w-0 flex-1 basis-72">
            <h2 className="text-sm font-bold text-[var(--danger-strong)]">Tehlikeli bölge</h2>
            <p className="text-meta mt-1">Bu işlemler onay ister ve denetim günlüğüne yazılır.</p>
          </div>
          <div className="flex flex-wrap gap-2">
            {dangerActions.map((kind) => (
              <button key={kind} type="button" disabled={openRun} onClick={() => { action.reset(); setConfirming(kind); }} className="btn btn-quiet !border-[var(--danger)]/60 !text-[var(--danger-strong)]">
                <Icon name="trash" className="h-4 w-4" />{ACTIONS[kind].label}
              </button>
            ))}
          </div>
        </section>
      )}

      {config && confirming && (
        <ConfirmDialog
          open
          title={config.title}
          description={school.displayName}
          confirmLabel={config.label}
          tone={config.danger ? "danger" : "primary"}
          confirmText={config.typeSlug ? school.slug : undefined}
          pending={action.isPending}
          error={action.error ? errorMessage(action.error) : null}
          onConfirm={() => runAction(confirming)}
          onClose={() => setConfirming(null)}
        >
          <p className="text-body">{config.description}</p>
        </ConfirmDialog>
      )}

      <UpgradeDialog
        open={upgradeOpen}
        schoolId={id}
        currentVersion={school.appVersion}
        onClose={() => setUpgradeOpen(false)}
        onQueued={(version) => { setUpgradeOpen(false); setNotice(`v${version} güncellemesi sıraya alındı.`); setTab("runs"); }}
      />
    </div>
  );
}

// Tek okul güncellemesi: yalnızca mevcut sürümden yeni ve geri çekilmemiş sürümler (§16.4).
function UpgradeDialog({ open, schoolId, currentVersion, onClose, onQueued }: {
  open: boolean; schoolId: string; currentVersion: string | null; onClose: () => void; onQueued: (version: string) => void;
}) {
  const releases = useReleases();
  const upgrade = useUpgradeSchool(schoolId);
  const [version, setVersion] = useState("");
  const newer = (releases.data ?? []).filter((release) => !release.isWithdrawn && (!currentVersion || compareVersions(release.version, currentVersion) > 0));
  const selected = version || newer[0]?.version || "";

  return (
    <Modal open={open} title="Okulu güncelle" description={currentVersion ? `Şu anki sürüm v${currentVersion}` : undefined} onClose={onClose} size="sm">
      <form
        className="grid gap-3"
        onSubmit={(event) => {
          event.preventDefault();
          if (selected) upgrade.mutate(selected, { onSuccess: () => onQueued(selected) });
        }}
      >
        {releases.isLoading ? <Skeleton className="h-10" /> : newer.length === 0 ? (
          <p className="text-meta">Daha yeni bir sürüm yok.</p>
        ) : (
          <label className="form-label">
            Hedef sürüm
            <select className="field font-mono" value={selected} onChange={(event) => setVersion(event.target.value)}>
              {newer.map((release) => <option key={release.version} value={release.version}>v{release.version}</option>)}
            </select>
          </label>
        )}
        <p className="text-meta">Güncelleme sonrası sağlık kontrolü düşerse ajan önceki imaja döner. Geri sürüme geçiş buradan yapılamaz.</p>
        {upgrade.error && <FormMessage tone="error">{errorMessage(upgrade.error)}</FormMessage>}
        <div className="flex justify-end gap-2">
          <button type="button" className="btn btn-quiet" onClick={onClose}>Vazgeç</button>
          <button type="submit" className="btn btn-primary" disabled={!selected || upgrade.isPending}>{upgrade.isPending ? "Gönderiliyor…" : "Güncelle"}</button>
        </div>
      </form>
    </Modal>
  );
}
