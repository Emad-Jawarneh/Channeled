import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { Button } from "../../ui/Button";
import styles from "./LoginPage.module.css";

export function LoginPage() {
  const navigate = useNavigate();
  const { login } = useAuth();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError(null);

    try {
      await login(email, password);
      navigate("/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sign in failed.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className={styles.page}>
      <form className={styles.form} onSubmit={onSubmit} data-testid="login-form">
        <p className={styles.brandLink}>
          <Link to="/">Channeled</Link>
        </p>
        <h1>Log in</h1>
        <p className={styles.hint}>Sign in to your Channeled account.</p>
        <label className={styles.label}>
          Email
          <input
            className={styles.input}
            data-testid="login-email"
            type="email"
            autoComplete="username"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            required
          />
        </label>
        <label className={styles.label}>
          Password
          <input
            className={styles.input}
            data-testid="login-password"
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={12}
          />
        </label>
        {error && (
          <p className={styles.error} data-testid="login-error">
            {error}
          </p>
        )}
        <Button type="submit" variant="primary" disabled={loading} data-testid="login-submit">
          {loading ? "Signing in…" : "Log in"}
        </Button>
        <p className={styles.footer}>
          Need an account? <Link to="/signup">Sign up</Link>
        </p>
      </form>
    </main>
  );
}
