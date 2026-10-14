import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
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
import { TeamMark } from "./TeamMark";
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
  const navigate = useNavigate();
  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });

  const myRole = team.data?.myRole ?? MemberRole.Viewer;
  const isAdmin = myRole >= MemberRole.Admin;
  const isOwner = myRole === MemberRole.Owner;

  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [openJoinConfirm, setOpenJoinConfirm] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);
  const [deleteOpen, setDeleteOpen] = useState(false);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [logoBusy, setLogoBusy] = useState(false);
  const logoInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (team.data) {
      setName(team.data.name);
      setDescription(team.data.description ?? "");
    }
  }, [team.data?.name, team.data?.description]); // eslint-disable-line react-hooks/exhaustive-deps

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

  async function onLogoSelected(file: File | null) {
    if (!file || !isAdmin) return;
    setLogoBusy(true);
    setError(null);
    setNotice(null);
    try {
      const updated = await teamsApi.setLogo(teamSlug, file);
      qc.setQueryData(["team", teamSlug], updated);
      await qc.invalidateQueries({ queryKey: ["teams"] });
      setNotice("Team logo updated.");
    } catch (e) {
      setError(errorMessage(e, "Could not update the logo."));
    } finally {
      setLogoBusy(false);
      if (logoInputRef.current) logoInputRef.current.value = "";
    }
  }

  async function onClearLogo() {
    if (!isAdmin) return;
    setLogoBusy(true);
    setError(null);
    setNotice(null);
    try {
      const updated = await teamsApi.clearLogo(teamSlug);
      qc.setQueryData(["team", teamSlug], updated);
      await qc.invalidateQueries({ queryKey: ["teams"] });
      setNotice("Team logo removed.");
    } catch (e) {
      setError(errorMessage(e, "Could not remove the logo."));
    } finally {
      setLogoBusy(false);
    }
  }

  const deleteTeam = useMutation({
    mutationFn: () => teamsApi.delete(teamSlug),
    onSuccess: async () => {
      setDeleteOpen(false);
      await qc.invalidateQueries({ queryKey: ["teams"] });
      qc.removeQueries({ queryKey: ["team", teamSlug] });
      navigate("/");
    },
    onError: (e) => setDeleteError(errorMessage(e, "Could not delete the team.")),
  });

  return (
    <AppShell title={`${team.data?.name ?? teamSlug} · General`}>
      {team.isLoading && <p className={styles.muted}>Loading…</p>}
      {team.data && (
        <div className={styles.page}>
          <header className={styles.pageHeader}>
            <p className={styles.eyebrow}>Team settings</p>
            <h1 className={styles.pageTitle}>General</h1>
            <p className={styles.pageLede}>
              Brand the team, edit its details, and control how people join.
            </p>
          </header>

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

          <section className={styles.card} data-testid="team-profile-card">
            <h2 className={styles.cardTitle}>Team details</h2>
            <p className={styles.cardIntro}>
              The logo appears in the team rail and sidebars. Name and description identify the team.
              Square images work best.
            </p>
            <div className={styles.profile}>
              <div className={styles.logoBlock}>
                <TeamMark team={team.data} size="lg" />
                {isAdmin && (
                  <>
                    <input
                      ref={logoInputRef}
                      type="file"
                      accept="image/png,image/jpeg,image/webp,image/gif"
                      hidden
                      data-testid="team-logo-input"
                      onChange={(e) => void onLogoSelected(e.target.files?.[0] ?? null)}
                    />
                    <div className={styles.logoActions}>
                      <Button
                        type="button"
                        data-testid="team-logo-upload"
                        disabled={logoBusy || save.isPending}
                        onClick={() => logoInputRef.current?.click()}
                      >
                        {logoBusy ? "Working…" : team.data.hasLogo ? "Replace logo" : "Upload logo"}
                      </Button>
                      {team.data.hasLogo && (
                        <Button
                          type="button"
                          data-testid="team-logo-remove"
                          disabled={logoBusy || save.isPending}
                          onClick={() => void onClearLogo()}
                        >
                          Remove
                        </Button>
                      )}
                    </div>
                    <p className={styles.logoHint}>PNG, JPEG, WebP, or GIF · max 512 KB</p>
                  </>
                )}
              </div>

              <form
                className={styles.profileFields}
                onSubmit={(e) => {
                  e.preventDefault();
                  const nextName = name.trim();
                  const nextDescription = description.trim();
                  if (!nextName) return;
                  const nameChanged = nextName !== team.data?.name;
                  const descriptionChanged = nextDescription !== (team.data?.description ?? "");
                  if (!nameChanged && !descriptionChanged) return;
                  void run(
                    {
                      name: nameChanged ? nextName : undefined,
                      description: descriptionChanged ? nextDescription : undefined,
                    },
                    "Team profile updated.",
                  );
                }}
              >
                <label className={styles.field}>
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
                <label className={styles.field}>
                  Description
                  <textarea
                    data-testid="team-description-input"
                    value={description}
                    onChange={(e) => setDescription(e.target.value)}
                    maxLength={2000}
                    placeholder="What is this team for?"
                    disabled={!isAdmin || save.isPending}
                  />
                </label>
                {isAdmin && (
                  <div className={styles.actions}>
                    <Button
                      type="submit"
                      variant="primary"
                      data-testid="team-profile-save"
                      disabled={
                        save.isPending ||
                        logoBusy ||
                        !name.trim() ||
                        (name.trim() === team.data.name &&
                          description.trim() === (team.data.description ?? ""))
                      }
                    >
                      Save details
                    </Button>
                  </div>
                )}
              </form>
            </div>
          </section>

          <section className={styles.card}>
            <h2 className={styles.cardTitle}>Joining</h2>
            <p className={styles.cardIntro}>
              Choose whether the join link admits people immediately or after an admin reviews them.
            </p>
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

          {isOwner && (
            <section className={`${styles.card} ${styles.dangerCard}`} data-testid="team-danger-zone">
              <h2 className={styles.cardTitle}>Danger zone</h2>
              <p className={styles.cardIntro}>
                Deleting a team permanently removes its channels, files, chat, and membership. This
                cannot be undone.
              </p>
              <div className={styles.actions}>
                <Button
                  type="button"
                  className={styles.dangerBtn}
                  data-testid="delete-team"
                  disabled={deleteTeam.isPending || save.isPending || logoBusy}
                  onClick={() => {
                    setDeleteError(null);
                    setDeleteOpen(true);
                  }}
                >
                  Delete team
                </Button>
              </div>
            </section>
          )}
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

      <ConfirmDialog
        open={deleteOpen}
        title="Delete this team?"
        confirmLabel="Delete team"
        requireText={team.data?.name}
        requireTextLabel={
          team.data ? `Type "${team.data.name}" to confirm` : "Type the team name to confirm"
        }
        busy={deleteTeam.isPending}
        error={deleteError}
        testId="delete-team-dialog"
        onClose={() => {
          if (!deleteTeam.isPending) setDeleteOpen(false);
        }}
        onConfirm={() => void deleteTeam.mutateAsync()}
      >
        <p>
          All channels, files, messages, and members for{" "}
          <strong>{team.data?.name ?? teamSlug}</strong> will be permanently removed. Share links
          stop working immediately.
        </p>
      </ConfirmDialog>
    </AppShell>
  );
}
