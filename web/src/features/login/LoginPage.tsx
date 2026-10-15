import { useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { Button } from "../../ui/Button";
import { AuthHero } from "./AuthHero";
import styles from "./LoginPage.module.css";

export function LoginPage() {
  const navigate = useNavigate();
  const location = useLocation();
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
      const from = (location.state as { from?: string } | null)?.from;
      navigate(from && from.startsWith("/") && !from.startsWith("//") ? from : "/");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Sign in failed.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className={styles.page}>
      <AuthHero
        title="Share files with your teams, on your own server."
        lede="Organized by team and channel, with clear permissions on every file."
      />

      <main className={styles.panel}>
        <form className={styles.form} onSubmit={onSubmit} data-testid="login-form">
          <h2>Welcome back</h2>
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
            {loading ? "Signing in…" : "Sign in"}
          </Button>
          <p className={styles.footer}>
            Need an account? <Link to="/signup">Sign up</Link>
          </p>
        </form>
      </main>
    </div>
  );
}
