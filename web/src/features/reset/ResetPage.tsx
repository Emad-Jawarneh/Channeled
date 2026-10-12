import { useState, type FormEvent } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { authApi } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

export function ResetPage() {
  const { token = "" } = useParams();
  const navigate = useNavigate();
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await authApi.resetPassword(token, password);
      navigate("/login");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Reset failed");
    }
  }

  return (
    <main className={styles.page}>
      <form className={styles.form} onSubmit={onSubmit}>
        <h1>Reset password</h1>
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
