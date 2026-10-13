import { useQuery } from "@tanstack/react-query";
import { teamsApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import styles from "./TeamGeneralSettingsPage.module.css";

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return `${value.toFixed(value >= 10 ? 0 : 1)} ${units[unit]}`;
}

export function TeamStorageSettingsPage({ teamSlug }: { teamSlug: string }) {
  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });
  const storage = useQuery({
    queryKey: ["team-storage", teamSlug],
    queryFn: () => teamsApi.storage(teamSlug),
  });

  const quota = storage.data?.quotaBytes ?? team.data?.quotaBytes ?? 0;
  const usage = storage.data?.usageBytes ?? 0;
  const pct = quota > 0 ? Math.min(100, Math.round((usage / quota) * 100)) : 0;

  return (
    <AppShell title={`${team.data?.name ?? teamSlug} · Storage`}>
      <div className={styles.page}>
        <header className={styles.pageHeader}>
          <p className={styles.eyebrow}>Team settings</p>
          <h1 className={styles.pageTitle}>Storage</h1>
          <p className={styles.pageLede}>How much of this team’s quota is in use across file channels.</p>
        </header>

        <section className={styles.card} data-testid="team-storage-card">
          {storage.isLoading && <p className={styles.muted}>Loading…</p>}
          {storage.isError && (
            <p className={styles.error} role="alert">
              Could not load storage usage.
            </p>
          )}
          {storage.isSuccess && (
            <>
              <div className={styles.storageHead}>
                <h2 className={styles.cardTitle}>Usage</h2>
                <p className={styles.storageFigures} data-testid="team-storage-figures">
                  <strong>{formatBytes(usage)}</strong>
                  <span> of {formatBytes(quota)}</span>
                </p>
              </div>
              <div
                className={styles.storageBar}
                role="progressbar"
                aria-valuemin={0}
                aria-valuemax={100}
                aria-valuenow={pct}
                aria-label="Storage used"
              >
                <span className={styles.storageFill} style={{ width: `${pct}%` }} />
              </div>
              <p className={styles.muted}>{pct}% of team quota used</p>
            </>
          )}
        </section>
      </div>
    </AppShell>
  );
}
