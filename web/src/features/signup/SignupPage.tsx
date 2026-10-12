import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";
import local from "./SignupPage.module.css";

export function SignupPage() {
  const navigate = useNavigate();
  const { register } = useAuth();
  const [email, setEmail] = useState("");
  const [displayName, setDisplayName] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError(null);
    try {
      await register(email, displayName, password);
      navigate("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sign up failed.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className={styles.page}>
      <form className={`${styles.form} ${local.form}`} onSubmit={(e) => void onSubmit(e)} data-testid="signup-form">
        <p className={local.eyebrow}>
          <Link to="/">Channeled</Link>
        </p>
        <h1 className={local.title}>Sign up</h1>
        <p className={styles.hint}>Create an account to start sharing files with your team.</p>
        <label className={styles.label}>
          Email
          <input
            className={styles.input}
            data-testid="signup-email"
            type="email"
            autoComplete="email"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </label>
        <label className={styles.label}>
          Display name
          <input
            className={styles.input}
            data-testid="signup-display-name"
            autoComplete="name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            required
          />
        </label>
        <label className={styles.label}>
          Password
          <input
            className={styles.input}
            data-testid="signup-password"
            type="password"
            autoComplete="new-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={12}
          />
        </label>
        {error && (
          <p className={styles.error} data-testid="signup-error">
            {error}
          </p>
        )}
        <Button type="submit" variant="primary" disabled={loading} data-testid="signup-submit">
          {loading ? "Creating account…" : "Create account"}
        </Button>
        <p className={local.footer}>
          Already have an account? <Link to="/login">Log in</Link>
        </p>
      </form>
    </main>
  );
}
