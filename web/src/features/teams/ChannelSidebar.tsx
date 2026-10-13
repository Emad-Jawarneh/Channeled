import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { ChannelKind, MemberRole, teamsApi, type Channel } from "../../api/channeled";
import styles from "../files/FileBrowser.module.css";
import { CreateChannelDialog } from "./CreateChannelDialog";

type Props = {
  teamSlug: string;
  channelName: string;
  trash?: boolean;
};

export function ChannelSidebar({ teamSlug, channelName, trash = false }: Props) {
  const navigate = useNavigate();
  const qc = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [creating, setCreating] = useState(false);

  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });
  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });

  const canCreateChannel = (team.data?.myRole ?? MemberRole.Viewer) >= MemberRole.Admin;
  const list = channels.data ?? [];
  const textChannels = list.filter((c) => (c.kind ?? ChannelKind.File) === ChannelKind.Text);
  const fileChannels = list.filter((c) => (c.kind ?? ChannelKind.File) === ChannelKind.File);

  function linkFor(ch: Channel) {
    const isFile = (ch.kind ?? ChannelKind.File) === ChannelKind.File;
    return `/t/${teamSlug}/c/${ch.name}${trash && isFile ? "/trash" : ""}`;
  }

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100%" }}>
      <div className={styles.sideHead}>
        <div className={styles.sideMark}>{teamSlug.slice(0, 1).toUpperCase()}</div>
        <div>
          <p className={styles.sideTitle}>{team.data?.name ?? teamSlug}</p>
          <p className={styles.sideMeta}>{list.length} channels</p>
        </div>
      </div>

      <p className={styles.sideLabel}>TEXT CHANNELS</p>
      <nav className={styles.channelNav} aria-label="Text channels">
        {textChannels.length === 0 && <p className={styles.sideEmpty}>No text channels yet</p>}
        {textChannels.map((ch) => (
          <a
            key={ch.id}
            href={linkFor(ch)}
            className={ch.name === channelName ? styles.active : undefined}
            data-testid={`channel-${ch.name}`}
          >
            # {ch.name}
          </a>
        ))}
      </nav>

      <p className={styles.sideLabel}>FILE CHANNELS</p>
      <nav className={styles.channelNav} aria-label="File channels">
        {fileChannels.length === 0 && <p className={styles.sideEmpty}>No file channels yet</p>}
        {fileChannels.map((ch) => (
          <a
            key={ch.id}
            href={linkFor(ch)}
            className={ch.name === channelName ? styles.active : undefined}
            data-testid={`channel-${ch.name}`}
          >
            # {ch.name}
          </a>
        ))}
      </nav>
      {canCreateChannel && (
        <button
          type="button"
          className={styles.newChannel}
          data-testid="new-channel"
          onClick={() => {
            setCreateError(null);
            setCreateOpen(true);
          }}
        >
          <span className={styles.newChannelPlus} aria-hidden="true">
            +
          </span>
          New channel
        </button>
      )}

      <nav className={styles.sideLinks} aria-label="Channel tools">
        {fileChannels.some((c) => c.name === channelName) && (
          <a href={`/t/${teamSlug}/c/${channelName}/trash`} data-testid="channel-trash-link">
            <svg className={styles.sideLinkIcon} viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
              <path d="M3 6h18" />
              <path d="M8 6V4h8v2" />
              <path d="M19 6l-1 14H6L5 6" />
              <path d="M10 11v6" />
              <path d="M14 11v6" />
            </svg>
            Trash
          </a>
        )}
        <a href={`/t/${teamSlug}/settings/members`}>
          <svg className={styles.sideLinkIcon} viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
            <circle cx="12" cy="12" r="3" />
            <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.8l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-1.8-.3 1.7 1.7 0 0 0-1 1.5V21a2 2 0 1 1-4 0v-.1a1.7 1.7 0 0 0-1-1.5 1.7 1.7 0 0 0-1.8.3l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0 .3-1.8 1.7 1.7 0 0 0-1.5-1H3a2 2 0 1 1 0-4h.1a1.7 1.7 0 0 0 1.5-1 1.7 1.7 0 0 0-.3-1.8l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.8.3H9a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 1 1.5 1.7 1.7 0 0 0 1.8-.3l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.8V9a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1z" />
          </svg>
          Team settings
        </a>
      </nav>

      <CreateChannelDialog
        open={createOpen}
        busy={creating}
        error={createError}
        onClose={() => {
          if (creating) return;
          setCreateOpen(false);
          setCreateError(null);
        }}
        onCreate={async ({ name, visibility, kind }) => {
          setCreating(true);
          setCreateError(null);
          try {
            const channel = await teamsApi.createChannel(teamSlug, {
              name,
              visibility,
              kind,
              description: null,
            });
            qc.setQueryData<Channel[]>(["channels", teamSlug], (prev) => {
              const list = prev ?? [];
              if (list.some((c) => c.id === channel.id)) {
                return list.map((c) => (c.id === channel.id ? channel : c));
              }
              return [...list, channel];
            });
            await qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
            setCreateOpen(false);
            navigate(`/t/${teamSlug}/c/${channel.name}`);
          } catch (e) {
            setCreateError(e instanceof Error ? e.message : "Could not create channel.");
          } finally {
            setCreating(false);
          }
        }}
      />
    </div>
  );
}
