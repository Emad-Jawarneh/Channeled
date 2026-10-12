import { useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { authApi, teamsApi } from "../../api/channeled";
import { clearCsrf, ensureCsrf } from "../../api/http";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

export function InvitePage() {
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const { refresh } = useAuth();
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [mode, setMode] = useState<"platform" | "team">("platform");

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      if (mode === "platform") {
        clearCsrf();
        await authApi.acceptInvite(token, displayName, password);
        await ensureCsrf();
        await refresh();
        navigate("/");
      } else {
        await teamsApi.acceptTeamInvite(token);
        await refresh();
        navigate("/");
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : "Invite failed");
    }
  }

  return (
    <main className={styles.page}>
      <form className={styles.form} onSubmit={onSubmit} data-testid="invite-form">
        <h1>Accept invitation</h1>
        <label className={styles.label}>
          Invite type
          <select value={mode} onChange={(e) => setMode(e.target.value as "platform" | "team")}>
            <option value="platform">New account</option>
            <option value="team">Existing account · team invite</option>
          </select>
        </label>
        {mode === "platform" && (
          <>
            <label className={styles.label}>
              Display name
              <input
                className={styles.input}
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                required
              />
            </label>
            <label className={styles.label}>
              Password
              <input
                className={styles.input}
                type="password"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                minLength={12}
              />
            </label>
          </>
        )}
        {error && <p className={styles.error}>{error}</p>}
        <Button type="submit" variant="primary">
          Accept
        </Button>
      </form>
    </main>
  );
}
