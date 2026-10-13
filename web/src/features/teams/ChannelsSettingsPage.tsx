import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ChannelKind,
  ChannelVisibility,
  MemberRole,
  teamsApi,
  type Channel,
  type TeamMember,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";
import { validateChannelName } from "./channelName";
import local from "./ChannelsSettingsPage.module.css";
import settingsStyles from "./TeamGeneralSettingsPage.module.css";
import { useUploads } from "../../uploads/UploadContext";
import { ConfirmDialog } from "./ConfirmDialog";
import { DeleteChannelDialog, type DeleteChannelDisposition } from "./DeleteChannelDialog";
import { ManageChannelMembersDialog } from "./ManageChannelMembersDialog";

/** Grants for a Restricted channel: compact list + Manage members dialog for adds. */
function RestrictedGrants({
  teamSlug,
  channel,
  teamMembers,
  onError,
}: {
  teamSlug: string;
  channel: Channel;
  teamMembers: TeamMember[];
  onError: (message: string | null) => void;
}) {
  const qc = useQueryClient();
  const [manageOpen, setManageOpen] = useState(false);
  const [dialogError, setDialogError] = useState<string | null>(null);
  const key = ["channel-members", teamSlug, channel.name] as const;

  const grants = useQuery({
    queryKey: key,
    queryFn: () => teamsApi.channelMembers(teamSlug, channel.name),
  });

  const fail = (e: unknown) => onError(e instanceof Error ? e.message : "Could not update access.");
  const refresh = () => void qc.invalidateQueries({ queryKey: key });

  const setReadOnly = useMutation({
    mutationFn: ({ userId, readOnly }: { userId: string; readOnly: boolean }) =>
      teamsApi.upsertChannelMember(teamSlug, channel.name, userId, readOnly),
    onSuccess: () => onError(null),
    onError: fail,
    onSettled: refresh,
  });
  const revoke = useMutation({
    mutationFn: (userId: string) => teamsApi.removeChannelMember(teamSlug, channel.name, userId),
    onSuccess: () => onError(null),
    onError: fail,
    onSettled: refresh,
  });
  const bulkAdd = useMutation({
    mutationFn: (input: { userIds: string[]; readOnly: boolean }) =>
      teamsApi.bulkUpsertChannelMembers(
        teamSlug,
        channel.name,
        input.userIds.map((userId) => ({ userId, readOnly: input.readOnly })),
      ),
    onSuccess: () => {
      setDialogError(null);
      onError(null);
      setManageOpen(false);
    },
    onError: (e) => {
      setDialogError(e instanceof Error ? e.message : "Could not update access.");
    },
    onSettled: refresh,
  });

  const granted = grants.data ?? [];
  const grantedIds = new Set(granted.map((g) => g.userId));
  // Admins and owners always see every channel, so they never need a grant.
  const candidates = teamMembers.filter((m) => !grantedIds.has(m.userId) && m.role < MemberRole.Admin);
  const busy = setReadOnly.isPending || revoke.isPending;

  return (
    <div className={local.accessPanel} data-testid={`grants-${channel.name}`}>
      <div className={local.accessHead}>
        <div className={local.accessTitles}>
          <p className={local.accessTitle}>Channel access</p>
          <p className={local.muted}>
            {grants.isLoading
              ? "Loading…"
              : granted.length === 0
                ? "No members granted yet. Team admins always have access."
                : `${granted.length} member${granted.length === 1 ? "" : "s"} with access · admins always included`}
          </p>
        </div>
        <Button
          type="button"
          variant="primary"
          data-testid={`grant-manage-${channel.name}`}
          disabled={grants.isLoading}
          onClick={() => {
            setDialogError(null);
            setManageOpen(true);
          }}
        >
          Manage members
        </Button>
      </div>

      {grants.isSuccess && granted.length > 0 && (
        <ul className={local.grantList}>
          {granted.map((g) => (
            <li key={g.userId} className={local.grantRow} data-testid={`grant-row-${channel.name}-${g.email}`}>
              <span className={local.grantIdentity}>
                <span className={local.grantName}>{g.displayName}</span>
                <span className={local.grantEmail}>{g.email}</span>
              </span>
              <span className={local.grantActions}>
                <label className={local.grantReadOnly}>
                  <input
                    type="checkbox"
                    data-testid={`grant-readonly-${channel.name}-${g.email}`}
                    checked={g.readOnly}
                    disabled={busy}
                    onChange={(e) =>
                      void setReadOnly.mutateAsync({ userId: g.userId, readOnly: e.target.checked })
                    }
                  />
                  Read-only
                </label>
                <Button
                  type="button"
                  data-testid={`grant-remove-${channel.name}-${g.email}`}
                  disabled={busy}
                  onClick={() => void revoke.mutateAsync(g.userId)}
                >
                  Remove
                </Button>
              </span>
            </li>
          ))}
        </ul>
      )}

      <ManageChannelMembersDialog
        open={manageOpen}
        channelName={channel.name}
        candidates={candidates}
        busy={bulkAdd.isPending}
        error={dialogError}
        onClose={() => {
          if (bulkAdd.isPending) return;
          setManageOpen(false);
          setDialogError(null);
        }}
        onAdd={(input) => void bulkAdd.mutateAsync(input)}
      />
    </div>
  );
}

export function ChannelsSettingsPage({ teamSlug }: { teamSlug: string }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const params = useParams();
  const uploads = useUploads();
  const [name, setName] = useState("");
  const [visibility, setVisibility] = useState<number>(ChannelVisibility.Everyone);
  const [kind, setKind] = useState<number>(ChannelKind.File);
  const [visTarget, setVisTarget] = useState<{ channel: Channel; to: number } | null>(null);
  const [visBusy, setVisBusy] = useState(false);
  const [visError, setVisError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [editName, setEditName] = useState("");
  const [editDescription, setEditDescription] = useState("");
  const [editError, setEditError] = useState<string | null>(null);
  const [deleting, setDeleting] = useState<Channel | null>(null);
  const [deleteError, setDeleteError] = useState<string | null>(null);
  const [deleteBusy, setDeleteBusy] = useState(false);
  const [actionError, setActionError] = useState<string | null>(null);

  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });

  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });

  const members = useQuery({
    queryKey: ["members", teamSlug],
    queryFn: () => teamsApi.members(teamSlug),
  });

  const canManage = (team.data?.myRole ?? MemberRole.Viewer) >= MemberRole.Admin;

  const create = useMutation({
    mutationFn: () =>
      teamsApi.createChannel(teamSlug, {
        name,
        visibility,
        kind,
        description: null,
      }),
    onSuccess: () => {
      setName("");
      setKind(ChannelKind.File);
      void qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
    },
  });

  async function applyVisibility() {
    if (!visTarget || visBusy) return;
    const { channel: ch, to } = visTarget;
    setVisBusy(true);
    setVisError(null);
    try {
      const result = await teamsApi.setChannelVisibility(teamSlug, ch.name, { visibility: to });
      setVisTarget(null);
      setActionError(null);
      setNotice(
        to === ChannelVisibility.Restricted
          ? `#${ch.name} is now Restricted. ${result.membersLosingAccess} member${
              result.membersLosingAccess === 1 ? "" : "s"
            } lost access; ${result.shareLinksRevoked} public share link${
              result.shareLinksRevoked === 1 ? "" : "s"
            } revoked.`
          : `#${ch.name} is now visible to everyone on the team.`,
      );
      await qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
      void qc.invalidateQueries({ queryKey: ["channel-members", teamSlug, ch.name] });
      void qc.invalidateQueries({ queryKey: ["items", teamSlug, ch.name] });
    } catch (e) {
      setVisError(e instanceof Error ? e.message : "Could not change visibility.");
    } finally {
      setVisBusy(false);
    }
  }

  function startEdit(ch: Channel) {
    setEditingId(ch.id);
    setEditName(ch.name);
    setEditDescription(ch.description ?? "");
    setEditError(null);
    setActionError(null);
  }

  async function saveEdit(ch: Channel) {
    const nameErr = validateChannelName(editName);
    if (nameErr) {
      setEditError(nameErr);
      return;
    }

    const nextName = editName.trim();
    const nextDescription = editDescription.trim();
    const nameChanged = nextName !== ch.name;
    const descriptionChanged = nextDescription !== (ch.description ?? "");
    if (!nameChanged && !descriptionChanged) {
      setEditingId(null);
      return;
    }

    setEditError(null);
    try {
      const updated = await teamsApi.updateChannel(teamSlug, ch.name, {
        name: nameChanged ? nextName : undefined,
        description: descriptionChanged ? nextDescription : undefined,
      });
      await qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
      if (nameChanged) {
        void qc.removeQueries({ queryKey: ["items", teamSlug, ch.name] });
        void qc.removeQueries({ queryKey: ["channel-members", teamSlug, ch.name] });
        void qc.removeQueries({ queryKey: ["messages", teamSlug, ch.name] });
        if (params.channel === ch.name) {
          void navigate(`/t/${teamSlug}/c/${updated.name}`, { replace: true });
        }
      }
      setEditingId(null);
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        setEditError("Name already taken");
      } else if (e instanceof ApiError && (e.status === 403 || e.status === 404)) {
        setEditError("Channel is no longer available. Refreshing…");
        void qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
        setEditingId(null);
      } else {
        setEditError(e instanceof Error ? e.message : "Could not save channel.");
      }
    }
  }

  async function runDelete(disposition: DeleteChannelDisposition) {
    if (!deleting || deleteBusy) return;
    setDeleteBusy(true);
    setDeleteError(null);
    const ch = deleting;
    try {
      const body =
        disposition.mode === "discard"
          ? { discardFiles: true }
          : disposition.mode === "move"
            ? { moveFilesToChannel: disposition.targetChannel }
            : undefined;
      await teamsApi.deleteChannel(teamSlug, ch.name, body);
      uploads.clearChannel(teamSlug, ch.name);
      void qc.removeQueries({ queryKey: ["items", teamSlug, ch.name] });
      void qc.removeQueries({ queryKey: ["channel-members", teamSlug, ch.name] });
      void qc.removeQueries({ queryKey: ["messages", teamSlug, ch.name] });
      await qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
      setDeleting(null);
      if (params.channel === ch.name) {
        const remaining = (channels.data ?? []).filter((c) => c.id !== ch.id);
        const next = remaining[0];
        void navigate(next ? `/t/${teamSlug}/c/${next.name}` : `/t/${teamSlug}/settings/channels`);
      }
    } catch (e) {
      // 404 after a concurrent delete: treat as success if the channel is already gone.
      if (e instanceof ApiError && e.status === 404) {
        await qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
        const list = qc.getQueryData<Channel[]>(["channels", teamSlug]);
        if (list && !list.some((c) => c.id === ch.id)) {
          uploads.clearChannel(teamSlug, ch.name);
          setDeleting(null);
          if (params.channel === ch.name) {
            const next = list[0];
            void navigate(next ? `/t/${teamSlug}/c/${next.name}` : `/t/${teamSlug}/settings/channels`);
          }
          return;
        }
        setDeleteError(e.message || "Channel is no longer available. Refreshing…");
        return;
      }
      setDeleteError(e instanceof Error ? e.message : "Could not delete channel.");
    } finally {
      setDeleteBusy(false);
    }
  }

  async function runArchive() {
    if (!deleting) return;
    setDeleteBusy(true);
    setDeleteError(null);
    try {
      await teamsApi.archiveChannel(teamSlug, deleting.name);
      await qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
      setDeleting(null);
    } catch (e) {
      setDeleteError(e instanceof Error ? e.message : "Could not archive channel.");
    } finally {
      setDeleteBusy(false);
    }
  }

  const list = channels.data ?? [];
  const otherFileChannels = (target: Channel) =>
    list.filter(
      (c) => c.id !== target.id && (c.kind ?? ChannelKind.File) === ChannelKind.File && !c.archivedAt,
    );

  return (
    <AppShell title={`${team.data?.name ?? teamSlug} · Channels`}>
      <div className={settingsStyles.page} style={{ maxWidth: "64rem" }}>
        <header className={settingsStyles.pageHeader}>
          <p className={settingsStyles.eyebrow}>Team settings</p>
          <h1 className={settingsStyles.pageTitle}>Channels</h1>
          <p className={settingsStyles.pageLede}>
            Create channels, change visibility, and manage who can access restricted ones.
          </p>
        </header>

      {actionError && (
        <p className={local.error} role="alert" aria-live="assertive">
          {actionError}
        </p>
      )}

      {notice && (
        <p className={local.muted} role="status" data-testid="channels-notice">
          {notice}
        </p>
      )}

      {canManage && (
        <form
          className={styles.toolbar}
          onSubmit={(e) => {
            e.preventDefault();
            void create.mutateAsync();
          }}
        >
          <label>
            Channel name
            <input data-testid="channel-name" value={name} onChange={(e) => setName(e.target.value)} required />
          </label>
          <label>
            Type
            <select data-testid="channel-kind" value={kind} onChange={(e) => setKind(Number(e.target.value))}>
              <option value={String(ChannelKind.File)}>File</option>
              <option value={String(ChannelKind.Text)}>Text</option>
            </select>
          </label>
          <label>
            Visibility
            <select
              data-testid="channel-visibility"
              value={visibility}
              onChange={(e) => setVisibility(Number(e.target.value))}
            >
              <option value={ChannelVisibility.Everyone}>Everyone</option>
              <option value={ChannelVisibility.Restricted}>Restricted</option>
            </select>
          </label>
          <Button type="submit" variant="primary" data-testid="create-channel">
            Create channel
          </Button>
        </form>
      )}

      <ul data-testid="channels-list" className={local.list}>
        {list.map((ch) => (
          <li key={ch.id} data-testid={`settings-channel-${ch.name}`} className={local.item}>
            <div className={local.itemHead}>
              <strong>#{ch.name}</strong>
              <span className={local.kindPill}>
                {(ch.kind ?? ChannelKind.File) === ChannelKind.Text ? "text" : "file"}
              </span>
              <span className={local.muted} data-testid={`visibility-label-${ch.name}`}>
                {ch.visibility === ChannelVisibility.Restricted ? "Restricted" : "Everyone"}
              </span>
              {ch.archivedAt && <span className={local.muted}>archived</span>}
              {canManage && editingId !== ch.id && (
                <span className={local.itemActions}>
                  <Button type="button" data-testid={`edit-channel-${ch.name}`} onClick={() => startEdit(ch)}>
                    Edit
                  </Button>
                  <Button
                    type="button"
                    data-testid={`change-visibility-${ch.name}`}
                    onClick={() => {
                      setVisError(null);
                      setVisTarget({
                        channel: ch,
                        to:
                          ch.visibility === ChannelVisibility.Restricted
                            ? ChannelVisibility.Everyone
                            : ChannelVisibility.Restricted,
                      });
                    }}
                  >
                    {ch.visibility === ChannelVisibility.Restricted ? "Make Everyone" : "Make Restricted"}
                  </Button>
                  <Button
                    type="button"
                    data-testid={`delete-channel-${ch.name}`}
                    aria-label={`Delete channel ${ch.name}`}
                    onClick={() => {
                      setDeleteError(null);
                      setDeleting(ch);
                    }}
                  >
                    Delete
                  </Button>
                </span>
              )}
            </div>

            {editingId === ch.id && (
              <div className={local.editForm} data-testid={`edit-channel-form-${ch.name}`}>
                <label>
                  Name
                  <input
                    data-testid={`edit-channel-name-${ch.name}`}
                    value={editName}
                    onChange={(e) => setEditName(e.target.value)}
                    maxLength={200}
                    disabled={create.isPending}
                  />
                </label>
                <label>
                  Description
                  <input
                    data-testid={`edit-channel-description-${ch.name}`}
                    value={editDescription}
                    onChange={(e) => setEditDescription(e.target.value)}
                    maxLength={2000}
                  />
                </label>
                <p className={local.muted}>
                  Type: {(ch.kind ?? ChannelKind.File) === ChannelKind.Text ? "text" : "file"} (read-only) · Visibility:{" "}
                  {ch.visibility === ChannelVisibility.Restricted ? "Restricted" : "Everyone"} (use Make
                  Restricted / Make Everyone to change)
                </p>
                {editError && (
                  <p className={local.error} role="alert" aria-live="assertive" data-testid={`edit-channel-error-${ch.name}`}>
                    {editError}
                  </p>
                )}
                <span className={local.itemActions}>
                  <Button type="button" onClick={() => setEditingId(null)}>
                    Cancel
                  </Button>
                  <Button
                    type="button"
                    variant="primary"
                    data-testid={`save-channel-${ch.name}`}
                    onClick={() => void saveEdit(ch)}
                  >
                    Save
                  </Button>
                </span>
              </div>
            )}

            {ch.visibility === ChannelVisibility.Restricted && canManage && (
              <RestrictedGrants
                teamSlug={teamSlug}
                channel={ch}
                teamMembers={members.data ?? []}
                onError={setActionError}
              />
            )}
          </li>
        ))}
      </ul>

      </div>

      <ConfirmDialog
        open={visTarget !== null}
        title={
          visTarget?.to === ChannelVisibility.Restricted
            ? `Restrict #${visTarget.channel.name}?`
            : `Make #${visTarget?.channel.name ?? ""} visible to everyone?`
        }
        confirmLabel={visTarget?.to === ChannelVisibility.Restricted ? "Make Restricted" : "Make Everyone"}
        busy={visBusy}
        error={visError}
        testId="visibility-dialog"
        onClose={() => {
          if (visBusy) return;
          setVisTarget(null);
          setVisError(null);
        }}
        onConfirm={() => void applyVisibility()}
      >
        {visTarget?.to === ChannelVisibility.Restricted ? (
          <p>
            Members without a grant will lose access. Public share links from those members will be revoked.
            Team admins keep access.
          </p>
        ) : (
          <p>
            Every team member will be able to see this channel. Existing grants are kept but have no effect while
            the channel is visible to everyone.
          </p>
        )}
      </ConfirmDialog>

      <DeleteChannelDialog
        open={deleting != null}
        channel={deleting}
        otherFileChannels={deleting ? otherFileChannels(deleting) : []}
        busy={deleteBusy}
        error={deleteError}
        onClose={() => {
          if (deleteBusy) return;
          setDeleting(null);
          setDeleteError(null);
        }}
        onDelete={runDelete}
        onArchive={runArchive}
      />
    </AppShell>
  );
}
