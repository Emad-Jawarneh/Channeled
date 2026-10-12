import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, useNavigate, useParams } from "react-router-dom";
import { ChannelVisibility, teamsApi } from "../../api/channeled";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

export function JoinChannelPage() {
  const { team = "", channel = "" } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();

  const info = useQuery({
    queryKey: ["channel-join", team, channel],
    queryFn: () => teamsApi.channelJoinInfo(team, channel),
    enabled: !!team && !!channel,
  });

  const request = useMutation({
    mutationFn: () => teamsApi.requestChannelJoin(team, channel),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["channel-join", team, channel] }),
  });

  if (info.isLoading) {
    return (
      <main className={styles.page}>
        <p>Loading…</p>
      </main>
    );
  }

  if (info.isError || !info.data) {
    return (
      <main className={styles.page}>
        <div className={styles.form}>
          <div className={styles.heroTop}>
            <h1 style={{ margin: 0 }}>Join channel</h1>
            <ThemeToggle compact />
          </div>
          <p className={styles.error}>This join link is invalid or you are not a member of the team.</p>
          <Link to="/">Back home</Link>
        </div>
      </main>
    );
  }

  const data = info.data;

  if (data.hasAccess) {
    return (
      <main className={styles.page}>
        <div className={styles.form} data-testid="join-already-member">
          <div className={styles.heroTop}>
            <h1 style={{ margin: 0 }}>#{data.channelName}</h1>
            <ThemeToggle compact />
          </div>
          <p className={styles.hint}>You already have access to this channel.</p>
          <Button type="button" variant="primary" onClick={() => navigate(`/t/${data.teamSlug}/c/${data.channelName}`)}>
            Open channel
          </Button>
        </div>
      </main>
    );
  }

  if (data.visibility !== ChannelVisibility.Private) {
    return (
      <main className={styles.page}>
        <div className={styles.form}>
          <div className={styles.heroTop}>
            <h1 style={{ margin: 0 }}>#{data.channelName}</h1>
            <ThemeToggle compact />
          </div>
          <p className={styles.hint}>Open channels are available to all team members.</p>
          <Button type="button" variant="primary" onClick={() => navigate(`/t/${data.teamSlug}/c/${data.channelName}`)}>
            Open channel
          </Button>
        </div>
      </main>
    );
  }

  const pending = !!data.pendingRequestId;

  return (
    <main className={styles.page}>
      <div className={styles.form} data-testid="join-channel-page">
        <div className={styles.heroTop}>
          <h1 style={{ margin: 0 }}>Request access</h1>
          <ThemeToggle compact />
        </div>
        <p className={styles.hint}>
          #{data.channelName} is private. A channel admin will approve or reject your request.
        </p>
        {pending ? (
          <p data-testid="join-pending">Your request is pending approval.</p>
        ) : (
          <Button
            type="button"
            variant="primary"
            data-testid="join-request"
            disabled={request.isPending}
            onClick={() => void request.mutateAsync()}
          >
            {request.isPending ? "Sending…" : "Request to join"}
          </Button>
        )}
        {request.isError && (
          <p className={styles.error}>{request.error instanceof Error ? request.error.message : "Request failed"}</p>
        )}
      </div>
    </main>
  );
}
