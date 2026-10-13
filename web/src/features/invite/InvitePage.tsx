import { useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { authApi } from "../../api/channeled";
import { clearCsrf, ensureCsrf } from "../../api/http";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

/** Platform (new account) invitations only. Team email invitations were replaced by the team join link. */
export function InvitePage() {
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const { refresh } = useAuth();
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      clearCsrf();
      await authApi.acceptInvite(token, displayName, password);
      await ensureCsrf();
      await refresh();
      navigate("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Invite failed");
    }
  }

  return (
    <main className={styles.page}>
      <form className={styles.form} onSubmit={onSubmit} data-testid="invite-form">
        <div className={styles.heroTop}>
          <h1 style={{ margin: 0 }}>Accept invitation</h1>
          <ThemeToggle compact />
        </div>
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
        {error && <p className={styles.error}>{error}</p>}
        <Button type="submit" variant="primary">
          Accept
        </Button>
      </form>
    </main>
  );
}
