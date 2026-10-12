import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { ChannelVisibility, teamsApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";

export function ChannelsSettingsPage({ teamSlug }: { teamSlug: string }) {
  const qc = useQueryClient();
  const [name, setName] = useState("");
  const [visibility, setVisibility] = useState<number>(ChannelVisibility.Open);

  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });

  const members = useQuery({
    queryKey: ["members", teamSlug],
    queryFn: () => teamsApi.members(teamSlug),
  });

  const create = useMutation({
    mutationFn: () => teamsApi.createChannel(teamSlug, name, visibility),
    onSuccess: () => {
      setName("");
      void qc.invalidateQueries({ queryKey: ["channels", teamSlug] });
    },
  });

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

      <ul data-testid="channels-list">
        {(channels.data ?? []).map((ch) => (
          <li key={ch.id} data-testid={`settings-channel-${ch.name}`}>
            #{ch.name} · {ch.visibility === ChannelVisibility.Private ? "private" : "open"}
            {ch.visibility === ChannelVisibility.Private && (
              <div>
                Grant access:
                <select
                  data-testid={`grant-${ch.name}`}
                  defaultValue=""
                  onChange={(e) => {
                    const userId = e.target.value;
                    if (!userId) return;
                    void teamsApi.upsertChannelMember(teamSlug, ch.name, userId, true, false);
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
            )}
          </li>
        ))}
      </ul>
    </AppShell>
  );
}
