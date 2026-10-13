import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import {
  ChannelKind,
  ChannelVisibility,
  MemberRole,
  teamsApi,
  type Channel,
  type ChannelJoinRequest,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";
import { validateChannelName } from "./channelName";
import local from "./ChannelsSettingsPage.module.css";
import { useUploads } from "../../uploads/UploadContext";
import { DeleteChannelDialog, type DeleteChannelDisposition } from "./DeleteChannelDialog";

function JoinRequests({ teamSlug, channel }: { teamSlug: string; channel: Channel }) {
  const qc = useQueryClient();
  const requests = useQuery({
    queryKey: ["join-requests", teamSlug, channel.name],
    queryFn: () => teamsApi.listChannelJoinRequests(teamSlug, channel.name),
  });

  const decide = useMutation({
    mutationFn: async ({ id, action }: { id: string; action: "approve" | "reject" }) => {
      if (action === "approve") {
        await teamsApi.approveChannelJoin(teamSlug, channel.name, id);
      } else {
        await teamsApi.rejectChannelJoin(teamSlug, channel.name, id);
      }
    },
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["join-requests", teamSlug, channel.name] }),
  });

  const items = (requests.data ?? []) as ChannelJoinRequest[];

  return (
    <div className={local.joinBlock} data-testid={`join-requests-${channel.name}`}>
      <p className={local.label}>Pending join requests</p>
      {requests.isLoading && <p>Loading…</p>}
      {!requests.isLoading && items.length === 0 && <p className={local.muted}>No pending requests.</p>}
      <ul className={local.requestList}>
        {items.map((r) => (
          <li key={r.id} data-testid={`join-request-${r.email}`}>
            <span>
              {r.displayName} ({r.email})
            </span>
            <span className={local.requestActions}>
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
                data-testid={`reject-join-${r.email}`}
                disabled={decide.isPending}
                onClick={() => void decide.mutateAsync({ id: r.id, action: "reject" })}
              >
                Reject
              </Button>
            </span>
          </li>
        ))}
      </ul>
    </div>
  );
}

export function ChannelsSettingsPage({ teamSlug }: { teamSlug: string }) {
  const qc = useQueryClient();
  const navigate = useNavigate();
  const params = useParams();
  const uploads = useUploads();
  const [name, setName] = useState("");
  const [visibility, setVisibility] = useState<number>(ChannelVisibility.Open);
  const [kind, setKind] = useState<number>(ChannelKind.File);
  const [copied, setCopied] = useState<string | null>(null);
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

  function joinUrl(channelName: string) {
    return `${window.location.origin}/join/${teamSlug}/${channelName}`;
  }

  async function copyJoinLink(channelName: string) {
    const url = joinUrl(channelName);
    await navigator.clipboard.writeText(url);
    setCopied(channelName);
    window.setTimeout(() => setCopied((c) => (c === channelName ? null : c)), 2000);
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
        void qc.removeQueries({ queryKey: ["join-requests", teamSlug, ch.name] });
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
      void qc.removeQueries({ queryKey: ["join-requests", teamSlug, ch.name] });
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
    <AppShell title={`${teamSlug} · Channels`}>
      {actionError && (
        <p className={local.error} role="alert" aria-live="assertive">
          {actionError}
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
              <option value={ChannelVisibility.Open}>Open</option>
              <option value={ChannelVisibility.Private}>Private</option>
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
              <span className={local.muted}>
                {ch.visibility === ChannelVisibility.Private ? "private" : "open"}
              </span>
              {ch.archivedAt && <span className={local.muted}>archived</span>}
              {canManage && editingId !== ch.id && (
                <span className={local.itemActions}>
                  <Button type="button" data-testid={`edit-channel-${ch.name}`} onClick={() => startEdit(ch)}>
                    Edit
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
                  {ch.visibility === ChannelVisibility.Private ? "private" : "open"} (read-only)
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

            <div className={local.joinLink} data-testid={`join-link-${ch.name}`}>
              <p className={local.label}>Public join link</p>
              <code data-testid={`join-url-${ch.name}`}>{joinUrl(ch.name)}</code>
              <Button type="button" data-testid={`copy-join-${ch.name}`} onClick={() => void copyJoinLink(ch.name)}>
                {copied === ch.name ? "Copied" : "Copy link"}
              </Button>
              <p className={local.muted}>
                Anyone with this link who is on the team can request access
                {ch.visibility === ChannelVisibility.Private
                  ? "; channel admins approve or reject."
                  : " (open channels already include all team members)."}
              </p>
            </div>

            {ch.visibility === ChannelVisibility.Private && (
              <>
                <div>
                  Grant access:
                  <select
                    data-testid={`grant-${ch.name}`}
                    defaultValue=""
                    onChange={(e) => {
                      const userId = e.target.value;
                      if (!userId) return;
                      void teamsApi.upsertChannelMember(teamSlug, ch.name, userId, true, false);
                      e.target.value = "";
                    }}
                  >
                    <option value="">Select member</option>
                    {(members.data ?? []).map((m) => (
                      <option key={m.userId} value={m.userId}>
                        {m.email}
                      </option>
                    ))}
                  </select>
                </div>
                <JoinRequests teamSlug={teamSlug} channel={ch} />
              </>
            )}
          </li>
        ))}
      </ul>

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
