import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router-dom";
import { adminApi } from "../../api/channeled";
import { useAuth } from "../../auth/AuthContext";
import { AppShell } from "../../components/AppShell";
import { formatBytes } from "../../lib/formatBytes";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";
import local from "./AdminPages.module.css";

function errorMessage(e: unknown, fallback: string): string {
  return e instanceof Error && e.message ? e.message : fallback;
}

function AdminNav() {
  return (
    <>
      <p className={local.sideLabel}>ADMIN</p>
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
    </>
  );
}

export function AdminDashboardPage() {
  const dash = useQuery({ queryKey: ["admin-dash"], queryFn: () => adminApi.dashboard() });
  return (
    <AppShell title="Admin · Dashboard" sidebar={<AdminNav />}>
      {dash.isLoading && <p className={styles.lede}>Loading…</p>}
      {dash.data && (
        <dl className={local.stats} data-testid="admin-dashboard">
          <div className={local.stat}>
            <dt>Users</dt>
            <dd>{dash.data.userCount}</dd>
          </div>
          <div className={local.stat}>
            <dt>Teams</dt>
            <dd>{dash.data.teamCount}</dd>
          </div>
          <div className={local.stat}>
            <dt>Files</dt>
            <dd>{dash.data.fileCount}</dd>
          </div>
          <div className={local.stat}>
            <dt>Audit (24h)</dt>
            <dd>{dash.data.auditEventsLast24h}</dd>
          </div>
        </dl>
      )}
    </AppShell>
  );
}

type TeamRes = { teamId: string; action: "transfer" | "delete"; newOwnerUserId: string };

export function AdminUsersPage() {
  const { user: me } = useAuth();
  const qc = useQueryClient();
  const users = useQuery({ queryKey: ["admin-users"], queryFn: () => adminApi.users() });
  const [deleteId, setDeleteId] = useState<string | null>(null);
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [resolutions, setResolutions] = useState<Record<string, TeamRes>>({});

  const preview = useQuery({
    queryKey: ["admin-user-delete-preview", deleteId],
    queryFn: () => adminApi.deletePreview(deleteId!),
    enabled: !!deleteId,
  });

  const disable = useMutation({
    mutationFn: (id: string) => adminApi.disableUser(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["admin-users"] }),
  });
  const enable = useMutation({
    mutationFn: (id: string) => adminApi.enableUser(id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["admin-users"] }),
  });
  const del = useMutation({
    mutationFn: async () => {
      if (!deleteId || !preview.data) throw new Error("Missing preview");
      const teamResolutions = preview.data.soleOwnedTeams.map((t) => {
        const r = resolutions[t.teamId];
        if (!r) throw new Error(`Choose transfer or delete for ${t.name}`);
        if (r.action === "delete" && t.transferOnly) {
          throw new Error(`${t.name} is too large; transfer instead`);
        }
        return {
          teamId: t.teamId,
          action: r.action,
          newOwnerUserId: r.action === "transfer" ? r.newOwnerUserId : null,
        };
      });
      await adminApi.deleteUser(deleteId, { password, teamResolutions });
    },
    onSuccess: async () => {
      setDeleteId(null);
      setPassword("");
      setResolutions({});
      await qc.invalidateQueries({ queryKey: ["admin-users"] });
    },
    onError: (e) => setError(errorMessage(e, "Could not delete user.")),
  });

  return (
    <AppShell title="Admin · Users" sidebar={<AdminNav />}>
      <div className={styles.tableWrap}>
        <table className={styles.table} data-testid="admin-users">
          <thead>
            <tr>
              <th scope="col">Email</th>
              <th scope="col">Name</th>
              <th scope="col">Status</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            {(users.data ?? []).map((u) => {
              const isSelf = me?.id === u.id;
              return (
                <tr key={u.id}>
                  <td>{u.email}</td>
                  <td>{u.displayName}</td>
                  <td>{u.status}</td>
                  <td>
                    {!isSelf && u.status === "active" && (
                      <Button
                        type="button"
                        data-testid={`disable-user-${u.id}`}
                        onClick={() => void disable.mutateAsync(u.id)}
                      >
                        Disable
                      </Button>
                    )}
                    {!isSelf && u.status === "disabled" && (
                      <Button
                        type="button"
                        data-testid={`enable-user-${u.id}`}
                        onClick={() => void enable.mutateAsync(u.id)}
                      >
                        Re-enable
                      </Button>
                    )}
                    {!isSelf && (
                      <Button
                        type="button"
                        data-testid={`delete-user-${u.id}`}
                        onClick={() => {
                          setError(null);
                          setPassword("");
                          setResolutions({});
                          setDeleteId(u.id);
                        }}
                      >
                        Delete
                      </Button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>

      {deleteId && preview.data && (
        <div className={local.deletePanel} data-testid="admin-delete-user-dialog" role="dialog">
          <h2>Delete {preview.data.email}?</h2>
          {preview.data.isLastEnabledPlatformAdmin ? (
            <p role="alert">This is the last enabled platform admin and cannot be deleted.</p>
          ) : (
            <>
              <p>
                Personal space: {preview.data.personalFileCount} files (
                {formatBytes(preview.data.personalBytes)}) will be discarded
                {preview.data.hasAvatar ? ", including avatar" : ""}.
              </p>
              {preview.data.soleOwnedTeams.map((t) => (
                <fieldset key={t.teamId} data-testid={`resolve-team-${t.teamId}`}>
                  <legend>
                    {t.name} ({t.fileCount} files
                    {t.transferOnly ? " — transfer only, too large to delete here" : ""})
                  </legend>
                  <label>
                    <input
                      type="radio"
                      name={`team-${t.teamId}`}
                      checked={resolutions[t.teamId]?.action === "transfer"}
                      onChange={() =>
                        setResolutions((prev) => ({
                          ...prev,
                          [t.teamId]: {
                            teamId: t.teamId,
                            action: "transfer",
                            newOwnerUserId: t.eligibleOwners[0]?.userId ?? "",
                          },
                        }))
                      }
                    />{" "}
                    Transfer ownership
                  </label>
                  {resolutions[t.teamId]?.action === "transfer" && (
                    <select
                      value={resolutions[t.teamId]?.newOwnerUserId ?? ""}
                      onChange={(e) =>
                        setResolutions((prev) => ({
                          ...prev,
                          [t.teamId]: {
                            teamId: t.teamId,
                            action: "transfer",
                            newOwnerUserId: e.target.value,
                          },
                        }))
                      }
                    >
                      {t.eligibleOwners.map((m) => (
                        <option key={m.userId} value={m.userId}>
                          {m.displayName} ({m.email})
                        </option>
                      ))}
                    </select>
                  )}
                  {!t.transferOnly && (
                    <label>
                      <input
                        type="radio"
                        name={`team-${t.teamId}`}
                        checked={resolutions[t.teamId]?.action === "delete"}
                        onChange={() =>
                          setResolutions((prev) => ({
                            ...prev,
                            [t.teamId]: { teamId: t.teamId, action: "delete", newOwnerUserId: "" },
                          }))
                        }
                      />{" "}
                      Delete team entirely
                    </label>
                  )}
                </fieldset>
              ))}
              <label>
                Your password
                <input
                  type="password"
                  data-testid="admin-delete-password"
                  value={password}
                  onChange={(e) => setPassword(e.target.value)}
                />
              </label>
              {error && (
                <p role="alert" data-testid="admin-delete-error">
                  {error}
                </p>
              )}
              <div className={local.actions}>
                <Button type="button" onClick={() => setDeleteId(null)} disabled={del.isPending}>
                  Cancel
                </Button>
                <Button
                  type="button"
                  variant="primary"
                  data-testid="admin-delete-confirm"
                  disabled={
                    del.isPending ||
                    !password ||
                    preview.data.isLastEnabledPlatformAdmin
                  }
                  onClick={() => {
                    setError(null);
                    void del.mutateAsync();
                  }}
                >
                  {del.isPending ? "Deleting…" : "Delete user"}
                </Button>
              </div>
            </>
          )}
        </div>
      )}
    </AppShell>
  );
}

export function AdminTeamsPage() {
  const teams = useQuery({ queryKey: ["admin-teams"], queryFn: () => adminApi.teams() });
  return (
    <AppShell title="Admin · Teams" sidebar={<AdminNav />}>
      <ul data-testid="admin-teams" className={local.plainList}>
        {(teams.data ?? []).map((t) => (
          <li key={t.id}>
            <strong>{t.name}</strong> ({t.slug}) · usage {formatBytes(t.usageBytes)} /{" "}
            {formatBytes(t.quotaBytes)}
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
      <div className={styles.tableWrap}>
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
      </div>
    </AppShell>
  );
}

export function AdminHealthPage() {
  const health = useQuery({ queryKey: ["admin-health"], queryFn: () => adminApi.health() });
  return (
    <AppShell title="Admin · Health" sidebar={<AdminNav />}>
      <pre className={local.codeBlock} data-testid="admin-health">
        {JSON.stringify(health.data ?? {}, null, 2)}
      </pre>
    </AppShell>
  );
}

export function AdminStoragePage() {
  const dash = useQuery({ queryKey: ["admin-dash"], queryFn: () => adminApi.dashboard() });
  return (
    <AppShell title="Admin · Storage" sidebar={<AdminNav />}>
      <dl className={local.stats} data-testid="admin-storage">
        <div className={local.stat}>
          <dt>Total file size</dt>
          <dd>{dash.data ? formatBytes(dash.data.totalFileBytes) : "…"}</dd>
        </div>
      </dl>
    </AppShell>
  );
}
