import { useQuery, useQueryClient } from "@tanstack/react-query";
import { authApi } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./Account.module.css";

export function SessionsPage() {
  const qc = useQueryClient();
  const sessions = useQuery({ queryKey: ["sessions"], queryFn: () => authApi.sessions() });

  return (
    <>
      <h2>Sessions</h2>
      <p className={styles.hint}>Sign out unfamiliar devices.</p>
      <ul className={styles.sessionList}>
        {(sessions.data ?? []).map((s) => (
          <li key={s.id} className={styles.sessionRow}>
            <div>
              <strong>{s.isCurrent ? "This device" : s.ip || "Session"}</strong>
              <div className={styles.hint}>{s.userAgent}</div>
              <div className={styles.hint}>Last seen {new Date(s.lastSeenAt).toLocaleString()}</div>
            </div>
            {!s.isCurrent && (
              <Button
                type="button"
                onClick={async () => {
                  await authApi.revokeSession(s.id);
                  await qc.invalidateQueries({ queryKey: ["sessions"] });
                }}
              >
                Revoke
              </Button>
            )}
          </li>
        ))}
      </ul>
    </>
  );
}
