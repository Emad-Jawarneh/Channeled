import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { adminApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import styles from "../files/FileBrowser.module.css";

function AdminNav() {
  return (
    <nav className={styles.channelNav} aria-label="Admin">
      <Link to="/admin/dashboard">Dashboard</Link>
      <Link to="/admin/users">Users</Link>
      <Link to="/admin/teams">Teams</Link>
      <Link to="/admin/audit" data-testid="admin-audit-link">
        Audit
      </Link>
      <Link to="/admin/health">Health</Link>
      <Link to="/admin/storage">Storage</Link>
    </nav>
  );
}

export function AdminDashboardPage() {
  const dash = useQuery({ queryKey: ["admin-dash"], queryFn: () => adminApi.dashboard() });
  return (
    <AppShell title="Admin · Dashboard" sidebar={<AdminNav />}>
      {dash.isLoading && <p>Loading…</p>}
      {dash.data && (
        <dl data-testid="admin-dashboard">
          <dt>Users</dt>
          <dd>{dash.data.userCount}</dd>
          <dt>Teams</dt>
          <dd>{dash.data.teamCount}</dd>
          <dt>Files</dt>
          <dd>{dash.data.fileCount}</dd>
          <dt>Audit (24h)</dt>
          <dd>{dash.data.auditEventsLast24h}</dd>
        </dl>
      )}
    </AppShell>
  );
}

export function AdminUsersPage() {
  const users = useQuery({ queryKey: ["admin-users"], queryFn: () => adminApi.users() });
  return (
    <AppShell title="Admin · Users" sidebar={<AdminNav />}>
      <table className={styles.table} data-testid="admin-users">
        <thead>
          <tr>
            <th scope="col">Email</th>
            <th scope="col">Name</th>
            <th scope="col">Status</th>
          </tr>
        </thead>
        <tbody>
          {(users.data ?? []).map((u) => (
            <tr key={u.id}>
              <td>{u.email}</td>
              <td>{u.displayName}</td>
              <td>{u.status}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </AppShell>
  );
}

export function AdminTeamsPage() {
  const teams = useQuery({ queryKey: ["admin-teams"], queryFn: () => adminApi.teams() });
  return (
    <AppShell title="Admin · Teams" sidebar={<AdminNav />}>
      <ul data-testid="admin-teams">
        {(teams.data ?? []).map((t) => (
          <li key={t.id}>
            {t.name} ({t.slug}) · usage {t.usageBytes}/{t.quotaBytes}
          </li>
        ))}
      </ul>
    </AppShell>
  );
}

export function AdminAuditPage() {
  const audit = useQuery({ queryKey: ["admin-audit"], queryFn: () => adminApi.audit(100) });
  return (
    <AppShell title="Admin · Audit" sidebar={<AdminNav />}>
      <table className={styles.table} data-testid="admin-audit">
        <thead>
          <tr>
            <th scope="col">When</th>
            <th scope="col">Type</th>
            <th scope="col">Target</th>
          </tr>
        </thead>
        <tbody>
          {(audit.data?.items ?? []).map((e) => (
            <tr key={e.id} data-testid={`audit-${e.eventType}`}>
              <td>{new Date(e.occurredAt).toLocaleString()}</td>
              <td>{e.eventType}</td>
              <td>
                {e.targetType}:{e.targetId}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </AppShell>
  );
}

export function AdminHealthPage() {
  const health = useQuery({ queryKey: ["admin-health"], queryFn: () => adminApi.health() });
  return (
    <AppShell title="Admin · Health" sidebar={<AdminNav />}>
      <pre data-testid="admin-health">{JSON.stringify(health.data ?? {}, null, 2)}</pre>
    </AppShell>
  );
}

export function AdminStoragePage() {
  const dash = useQuery({ queryKey: ["admin-dash"], queryFn: () => adminApi.dashboard() });
  return (
    <AppShell title="Admin · Storage" sidebar={<AdminNav />}>
      <p data-testid="admin-storage">Total file bytes: {dash.data?.totalFileBytes ?? "…"}</p>
    </AppShell>
  );
}
