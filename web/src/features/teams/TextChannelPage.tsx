import { useQuery } from "@tanstack/react-query";
import { ChannelVisibility, teamsApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { ChannelSidebar } from "./ChannelSidebar";
import local from "./TextChannelPage.module.css";

type Props = {
  teamSlug: string;
  channelName: string;
};

export function TextChannelPage({ teamSlug, channelName }: Props) {
  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });

  const channel = (channels.data ?? []).find((c) => c.name === channelName);

  return (
    <AppShell
      title={`#${channelName}`}
      sidebar={<ChannelSidebar teamSlug={teamSlug} channelName={channelName} />}
    >
      <div className={local.hero}>
        <p className={local.kicker}>Text channel</p>
        <h2 className={local.title}>#{channelName}</h2>
        <p className={local.lede}>
          {channel?.description?.trim()
            ? channel.description
            : "A labeled space for channel context. Share files from a file channel."}
        </p>
        <dl className={local.meta}>
          <div>
            <dt>Visibility</dt>
            <dd>{channel?.visibility === ChannelVisibility.Private ? "Private" : "Open"}</dd>
          </div>
          <div>
            <dt>Kind</dt>
            <dd>Text</dd>
          </div>
        </dl>
        <p className={local.note} data-testid="text-channel-home">
          Messaging stays out of scope for Channeled. This channel is for organization and context only.
        </p>
      </div>
    </AppShell>
  );
}
