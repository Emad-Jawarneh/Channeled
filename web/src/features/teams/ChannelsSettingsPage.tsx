import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import {
  ChannelKind,
  ChannelVisibility,
  teamsApi,
  type Channel,
  type ChannelJoinRequest,
} from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";
import local from "./ChannelsSettingsPage.module.css";

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
  const [name, setName] = useState("");
  const [visibility, setVisibility] = useState<number>(ChannelVisibility.Open);
  const [kind, setKind] = useState<number>(ChannelKind.File);
  const [copied, setCopied] = useState<string | null>(null);

  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });

  const members = useQuery({
    queryKey: ["members", teamSlug],
    queryFn: () => teamsApi.members(teamSlug),
  });

  const create = useMutation({
    mutationFn: () => teamsApi.createChannel(teamSlug, name, visibility, null, kind),
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

  return (
    <AppShell title={`${teamSlug} · Channels`}>
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
            <option value={ChannelKind.File}>File</option>
            <option value={ChannelKind.Text}>Text</option>
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

      <ul data-testid="channels-list" className={local.list}>
        {(channels.data ?? []).map((ch) => (
          <li key={ch.id} data-testid={`settings-channel-${ch.name}`} className={local.item}>
            <div className={local.itemHead}>
              <strong>#{ch.name}</strong>
              <span>
                {(ch.kind ?? ChannelKind.File) === ChannelKind.Text ? "text" : "file"} ·{" "}
                {ch.visibility === ChannelVisibility.Private ? "private" : "open"}
              </span>
            </div>

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
    </AppShell>
  );
}
