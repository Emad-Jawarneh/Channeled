import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Link, useNavigate, useParams } from "react-router-dom";
import { TeamJoinMode, teamsApi } from "../../api/channeled";
import { ApiError } from "../../api/http";
import { useAuth } from "../../auth/AuthContext";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

function Shell({ heading, children }: { heading: string; children: ReactNode }) {
  return (
    <main className={`${styles.page} ${styles.pageCentered}`}>
      <div className={styles.form} data-testid="join-team-page">
        <div className={styles.heroTop}>
          <h1 className={styles.formHeading}>{heading}</h1>
          <ThemeToggle compact />
        </div>
        {children}
      </div>
    </main>
  );
}

/**
 * Team join link landing page. The info endpoint is anonymous-friendly (team name + join mode only);
 * joining or withdrawing needs a session, so signed-out visitors are sent to sign in and back.
 */
export function JoinTeamPage() {
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { user, loading: authLoading } = useAuth();

  const info = useQuery({
    queryKey: ["team-join", token, !!user],
    queryFn: () => teamsApi.getTeamJoinInfo(token),
    enabled: !!token && !authLoading,
    retry: false,
  });

  const join = useMutation({
    mutationFn: () => teamsApi.joinTeam(token),
    onSuccess: async (result) => {
      if (result.joined) {
        await qc.invalidateQueries({ queryKey: ["teams"] });
        navigate(`/t/${result.teamSlug}/c/general`, { replace: true });
      } else {
        await qc.invalidateQueries({ queryKey: ["team-join", token] });
      }
    },
  });

  const withdraw = useMutation({
    mutationFn: (requestId: string) => teamsApi.withdrawJoinRequest(requestId),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["team-join", token] }),
  });

  if (authLoading || info.isLoading) {
    return (
      <Shell heading="Join team">
        <p className={styles.hint}>Loading…</p>
      </Shell>
    );
  }

  if (info.isError || !info.data) {
    return (
      <Shell heading="Join team">
        <p className={styles.error} role="alert" data-testid="join-invalid">
          This join link is invalid or has expired.
        </p>
        <Link to="/">Back home</Link>
      </Shell>
    );
  }

  const data = info.data;
  const pendingId = data.pendingRequestId ?? null;
  const open = data.joinMode === TeamJoinMode.OpenJoin;
  const actionError = join.error ?? withdraw.error;
  const actionMessage = actionError
    ? actionError instanceof ApiError && actionError.status === 404
      ? "This join link is invalid or has expired."
      : actionError instanceof Error
        ? actionError.message
        : "Something went wrong."
    : null;

  let body: ReactNode;
  if (!user) {
    body = (
      <>
        <p className={styles.hint}>
          {open ? "Anyone with this link can join." : "Joining needs approval from a team admin."} Sign in to continue.
        </p>
        <Button
          type="button"
          variant="primary"
          data-testid="join-signin"
          onClick={() => navigate("/login", { state: { from: `/join/${token}` } })}
        >
          Sign in to {open ? "join" : "request to join"}
        </Button>
      </>
    );
  } else if (data.isMember) {
    body = (
      <>
        <p className={styles.hint} data-testid="join-already-member">
          You are already a member of {data.teamName}.
        </p>
        <Button
          type="button"
          variant="primary"
          data-testid="join-open-team"
          disabled={join.isPending}
          onClick={() => void join.mutateAsync()}
        >
          Open team
        </Button>
      </>
    );
  } else if (pendingId) {
    body = (
      <>
        <p className={styles.hint} data-testid="join-pending">
          Your request to join {data.teamName} is pending approval.
        </p>
        <Button
          type="button"
          data-testid="join-withdraw"
          disabled={withdraw.isPending}
          onClick={() => void withdraw.mutateAsync(pendingId)}
        >
          {withdraw.isPending ? "Withdrawing…" : "Withdraw request"}
        </Button>
      </>
    );
  } else {
    body = (
      <>
        <p className={styles.hint}>
          {open
            ? "You will join right away."
            : "A team admin will approve or reject your request."}
        </p>
        <Button
          type="button"
          variant="primary"
          data-testid={open ? "join-team" : "join-request"}
          disabled={join.isPending}
          onClick={() => void join.mutateAsync()}
        >
          {join.isPending ? "Sending…" : open ? "Join team" : "Request to join"}
        </Button>
      </>
    );
  }

  return (
    <Shell heading={data.teamName}>
      <p className={styles.hint} data-testid="join-mode">
        {open ? "Open join" : "Request to join"}
      </p>
      {body}
      {actionMessage && (
        <p className={styles.error} role="alert" data-testid="join-error">
          {actionMessage}
        </p>
      )}
    </Shell>
  );
}
