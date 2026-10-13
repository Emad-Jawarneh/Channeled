import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useState } from "react";
import {
  MemberRole,
  TeamJoinMode,
  teamsApi,
  type TeamJoinLink,
  type TeamJoinRequest,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import { ConfirmDialog } from "./ConfirmDialog";
import styles from "./TeamGeneralSettingsPage.module.css";

function errorMessage(e: unknown, fallback: string): string {
  return e instanceof Error && e.message ? e.message : fallback;
}

/** `datetime-local` value (no zone) -> ISO string, or null when blank/invalid. */
function toIso(local: string): string | null {
  if (!local) return null;
  const d = new Date(local);
  return Number.isNaN(d.getTime()) ? null : d.toISOString();
}

function JoinLinkSection({ teamSlug }: { teamSlug: string }) {
  const qc = useQueryClient();
  const [copied, setCopied] = useState(false);
  const [expiry, setExpiry] = useState("");
  const [rotateOpen, setRotateOpen] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const link = useQuery<TeamJoinLink>({
    queryKey: ["join-link", teamSlug],
    retry: false,
    // The raw token is a secret: never keep it around longer than needed.
    gcTime: 0,
    queryFn: async () => {
      try {
        return await teamsApi.getJoinLink(teamSlug);
      } catch (e) {
        // No link yet (409 join_link_unavailable): create one.
        if (e instanceof ApiError && e.status === 409) {
          return teamsApi.ensureJoinLink(teamSlug);
        }
        throw e;
      }
    },
  });

  const rotate = useMutation({
    mutationFn: () => teamsApi.rotateJoinLink(teamSlug, toIso(expiry)),
    onSuccess: (next) => {
      qc.setQueryData(["join-link", teamSlug], next);
      setRotateOpen(false);
      setActionError(null);
    },
    onError: (e) => setActionError(errorMessage(e, "Could not rotate the join link.")),
  });

  const url = link.data ? `${window.location.origin}${link.data.joinPath}` : "";

  async function copy() {
    if (!url) return;
    await navigator.clipboard.writeText(url);
    setCopied(true);
    window.setTimeout(() => setCopied(false), 2000);
  }

  return (
    <section className={styles.card} data-testid="join-link-section">
      <h2 className={styles.cardTitle}>Join link</h2>
      {link.isLoading && <p className={styles.muted}>Loading…</p>}
      {link.isError && (
        <p className={styles.error} role="alert">
          The join link is unavailable. Rotate the link to create a new one.
        </p>
      )}
      {link.data && (
        <div className={styles.joinLink}>
          <code data-testid="team-join-url">{url}</code>
          <p className={styles.muted}>
            {link.data.expiresAt
              ? `Expires ${new Date(link.data.expiresAt).toLocaleString()}`
              : "Never expires"}
          </p>
          <div className={styles.actions}>
            <Button type="button" data-testid="copy-team-join" onClick={() => void copy()}>
              {copied ? "Copied" : "Copy link"}
            </Button>
          </div>
        </div>
      )}
      <label className={styles.field}>
        Optional expiry for the next rotated link
        <input
          type="datetime-local"
          data-testid="join-link-expiry"
          value={expiry}
          onChange={(e) => setExpiry(e.target.value)}
        />
      </label>
      <div className={styles.actions}>
        <Button
          type="button"
          data-testid="rotate-team-join"
          disabled={rotate.isPending}
          onClick={() => {
            setActionError(null);
            setRotateOpen(true);
          }}
        >
          Rotate link
        </Button>
      </div>
      {actionError && !rotateOpen && (
        <p className={styles.error} role="alert">
          {actionError}
        </p>
      )}
      <ConfirmDialog
        open={rotateOpen}
        title="Rotate join link?"
        confirmLabel="Rotate link"
        busy={rotate.isPending}
        error={actionError}
        testId="rotate-dialog"
        onClose={() => setRotateOpen(false)}
        onConfirm={() => void rotate.mutateAsync()}
      >
        <p>The current link stops working immediately. Anyone holding it will need the new one.</p>
      </ConfirmDialog>
    </section>
  );
}

function PendingRequests({
  teamSlug,
  defaultRole,
}: {
  teamSlug: string;
  defaultRole: number;
}) {
  const qc = useQueryClient();
  const [roles, setRoles] = useState<Record<string, number>>({});
  const [error, setError] = useState<string | null>(null);

  const requests = useQuery({
    queryKey: ["join-requests", teamSlug],
    queryFn: () => teamsApi.listJoinRequests(teamSlug),
  });

  function refresh() {
    void qc.invalidateQueries({ queryKey: ["join-requests", teamSlug] });
    void qc.invalidateQueries({ queryKey: ["notifications"] });
    void qc.invalidateQueries({ queryKey: ["members", teamSlug] });
  }

  const decide = useMutation({
    mutationFn: async ({ id, action }: { id: string; action: "approve" | "reject" }) => {
      if (action === "approve") {
        await teamsApi.approveJoinRequest(teamSlug, id, roles[id] ?? defaultRole);
      } else {
        await teamsApi.rejectJoinRequest(teamSlug, id);
      }
    },
    onSuccess: () => setError(null),
    onError: (e) => setError(errorMessage(e, "Could not update the request.")),
    onSettled: refresh,
  });

  const items: TeamJoinRequest[] = requests.data ?? [];

  return (
    <section className={styles.card} data-testid="pending-join-requests">
      <div className={styles.sectionHead}>
        <h2 className={styles.cardTitle}>Pending join requests</h2>
        {requests.isSuccess && (
          <span className={styles.count} data-testid="pending-join-count">
            {items.length === 0 ? "None" : `${items.length} waiting`}
          </span>
        )}
      </div>
      {requests.isLoading && <p className={styles.muted}>Loading…</p>}
      {requests.isSuccess && items.length === 0 && (
        <p className={styles.muted}>No one is waiting to join right now.</p>
      )}
      {items.length > 0 && (
        <ul className={styles.requestList}>
          {items.map((r) => (
            <li key={r.id} className={styles.requestRow} data-testid={`join-request-${r.email}`}>
              <div className={styles.person}>
                <span className={styles.personName}>{r.displayName}</span>
                <span className={styles.personEmail}>{r.email}</span>
              </div>
              <div className={styles.requestActions}>
                <select
                  className={styles.roleSelect}
                  aria-label={`Role for ${r.email}`}
                  data-testid={`join-role-${r.email}`}
                  value={roles[r.id] ?? defaultRole}
                  disabled={decide.isPending}
                  onChange={(e) => setRoles((prev) => ({ ...prev, [r.id]: Number(e.target.value) }))}
                >
                  <option value={MemberRole.Viewer}>Viewer</option>
                  <option value={MemberRole.Member}>Member</option>
                </select>
                <Button
                  type="button"
                  variant="primary"
                  data-testid={`approve-join-${r.email}`}
                  disabled={decide.isPending}
                  onClick={() => void decide.mutateAsync({ id: r.id, action: "approve" })}
                >
                  Approve
                </Button>
                <Button
                  type="button"
                  className={styles.rejectBtn}
                  data-testid={`reject-join-${r.email}`}
                  disabled={decide.isPending}
                  onClick={() => void decide.mutateAsync({ id: r.id, action: "reject" })}
                >
                  Reject
                </Button>
              </div>
            </li>
          ))}
        </ul>
      )}
      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}
    </section>
  );
}

export function TeamGeneralSettingsPage({ teamSlug }: { teamSlug: string }) {
  const qc = useQueryClient();
  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });

  const myRole = team.data?.myRole ?? MemberRole.Viewer;
  const isAdmin = myRole >= MemberRole.Admin;
  const isOwner = myRole === MemberRole.Owner;

  const [name, setName] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [openJoinConfirm, setOpenJoinConfirm] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);

  useEffect(() => {
    if (team.data) setName(team.data.name);
  }, [team.data?.name]); // eslint-disable-line react-hooks/exhaustive-deps

  const pending = useQuery({
    queryKey: ["join-requests", teamSlug],
    queryFn: () => teamsApi.listJoinRequests(teamSlug),
    enabled: isAdmin,
  });
  const pendingCount = pending.data?.length ?? 0;

  const save = useMutation({
    mutationFn: (input: Parameters<typeof teamsApi.updateSettings>[1]) =>
      teamsApi.updateSettings(teamSlug, input),
    onSuccess: async (result) => {
      qc.setQueryData(["team", teamSlug], result.team);
      await qc.invalidateQueries({ queryKey: ["teams"] });
      await qc.invalidateQueries({ queryKey: ["join-requests", teamSlug] });
      await qc.invalidateQueries({ queryKey: ["members", teamSlug] });
      await qc.invalidateQueries({ queryKey: ["notifications"] });
    },
  });

  async function run(input: Parameters<typeof teamsApi.updateSettings>[1], okMessage: string) {
    setError(null);
    setNotice(null);
    try {
      await save.mutateAsync(input);
      setNotice(okMessage);
    } catch (e) {
      setError(errorMessage(e, "Could not save settings."));
    }
  }

  async function confirmOpenJoin() {
    setDialogError(null);
    try {
      const result = await save.mutateAsync({
        joinMode: TeamJoinMode.OpenJoin,
        confirmOpenJoinApprovePending: true,
      });
      setOpenJoinConfirm(false);
      setError(null);
      const approved = result.approvedCount ?? 0;
      setNotice(
        approved > 0
          ? `Open join enabled. ${approved} pending request${approved === 1 ? "" : "s"} approved.`
          : "Open join enabled.",
      );
    } catch (e) {
      setDialogError(errorMessage(e, "Could not enable open join."));
    }
  }

  const joinMode = team.data?.joinMode ?? TeamJoinMode.RequestToJoin;
  const defaultRole = team.data?.defaultJoinRole ?? MemberRole.Viewer;
  const roleName = defaultRole === MemberRole.Member ? "Member" : "Viewer";

  return (
    <AppShell title={`${team.data?.name ?? teamSlug} · General`}>
      {team.isLoading && <p className={styles.muted}>Loading…</p>}
      {team.data && (
        <div className={styles.list}>
          {notice && (
            <p className={styles.muted} role="status" data-testid="settings-notice">
              {notice}
            </p>
          )}
          {error && (
            <p className={styles.error} role="alert" data-testid="settings-error">
              {error}
            </p>
          )}

          <section className={styles.card}>
            <h2 className={styles.cardTitle}>Team name</h2>
            <form
              className={styles.actions}
              onSubmit={(e) => {
                e.preventDefault();
                const next = name.trim();
                if (!next || next === team.data?.name) return;
                void run({ name: next }, "Team name updated.");
              }}
            >
              <label className={styles.field} style={{ flex: "1 1 14rem" }}>
                Name
                <input
                  data-testid="team-name-input"
                  value={name}
                  onChange={(e) => setName(e.target.value)}
                  maxLength={200}
                  required
                  disabled={!isAdmin || save.isPending}
                />
              </label>
              {isAdmin && (
                <Button
                  type="submit"
                  variant="primary"
                  data-testid="team-name-save"
                  disabled={save.isPending || !name.trim() || name.trim() === team.data.name}
                >
                  Save
                </Button>
              )}
            </form>
          </section>

          <section className={styles.card}>
            <h2 className={styles.cardTitle}>Joining</h2>
            <label className={styles.field}>
              Join mode
              <select
                className={styles.roleSelect}
                data-testid="join-mode-select"
                value={joinMode}
                disabled={!isOwner || save.isPending}
                onChange={(e) => {
                  const next = Number(e.target.value);
                  if (next === joinMode) return;
                  if (next === TeamJoinMode.OpenJoin) {
                    setDialogError(null);
                    setOpenJoinConfirm(true);
                  } else {
                    void run({ joinMode: next }, "Join mode set to Request to join.");
                  }
                }}
              >
                <option value={TeamJoinMode.OpenJoin}>Open join</option>
                <option value={TeamJoinMode.RequestToJoin}>Request to join</option>
              </select>
            </label>
            <p className={styles.muted}>
              {joinMode === TeamJoinMode.OpenJoin
                ? `Anyone with the join link becomes a ${roleName} immediately.`
                : "People with the join link send a request; an admin approves or rejects it."}
              {!isOwner && " Only the owner can change this."}
            </p>
            <label className={styles.field}>
              Default join role
              <select
                className={styles.roleSelect}
                data-testid="default-join-role-select"
                value={defaultRole}
                disabled={!isOwner || save.isPending}
                onChange={(e) =>
                  void run({ defaultJoinRole: Number(e.target.value) }, "Default join role updated.")
                }
              >
                <option value={MemberRole.Viewer}>Viewer</option>
                <option value={MemberRole.Member}>Member</option>
              </select>
            </label>
          </section>

          {isAdmin && <JoinLinkSection teamSlug={teamSlug} />}
          {isAdmin && <PendingRequests teamSlug={teamSlug} defaultRole={defaultRole} />}
        </div>
      )}

      <ConfirmDialog
        open={openJoinConfirm}
        title="Switch to Open join?"
        confirmLabel="Enable open join"
        busy={save.isPending}
        error={dialogError}
        testId="open-join-dialog"
        onClose={() => setOpenJoinConfirm(false)}
        onConfirm={() => void confirmOpenJoin()}
      >
        <p data-testid="open-join-pending-count">
          {pendingCount} pending join request{pendingCount === 1 ? "" : "s"} will be approved as{" "}
          {roleName} when you confirm. From then on, anyone with the join link joins immediately.
        </p>
      </ConfirmDialog>
    </AppShell>
  );
}
