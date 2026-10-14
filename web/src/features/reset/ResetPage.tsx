import { useEffect, useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { authApi } from "../../api/channeled";
import { takeFragmentToken } from "../../lib/fragmentToken";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

export function ResetPage() {
  const { token: routeToken = "" } = useParams();
  const navigate = useNavigate();
  const [token, setToken] = useState(routeToken);
  const [manual, setManual] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const fromHash = takeFragmentToken();
    if (fromHash) setToken(fromHash);
  }, []);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const value = token || manual;
    if (!value) {
      setError("Paste the reset token from your email.");
      return;
    }
    try {
      await authApi.resetPassword(value, password);
      navigate("/login");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Reset failed");
    }
  }

  return (
    <main className={styles.page}>
      <form className={styles.form} onSubmit={onSubmit}>
        <div className={styles.heroTop}>
          <h1 style={{ margin: 0 }}>Reset password</h1>
          <ThemeToggle compact />
        </div>
        <p>Submitting the form is what changes your password — opening this page does nothing.</p>
        <label className={styles.label}>
          {token ? "Token (from email link)" : "Paste token"}
          <input
            className={styles.input}
            value={token || manual}
            onChange={(e) => {
              setToken("");
              setManual(e.target.value);
            }}
            required
          />
        </label>
        <label className={styles.label}>
          New password
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
          Update password
        </Button>
      </form>
    </main>
  );
}
