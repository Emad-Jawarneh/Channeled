import { useState, type FormEvent } from "react";
import { Link, useLocation, useNavigate } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
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
      <section className={styles.hero} aria-label="About Channeled">
        <div className={styles.heroTop}>
          <p className={styles.brandRow}>
            <span className={styles.logo}>C</span>
            Channeled
          </p>
          <ThemeToggle compact />
        </div>
        <div>
          <h1 className={styles.heroTitle}>Share files with your teams, on your own server.</h1>
          <p className={styles.heroLede}>
            Organized by team and channel, with clear permissions on every file.
          </p>
        </div>
        <div className={styles.heroPoints}>
          <p>Independent teams and channels</p>
          <p>Private, self-hosted storage</p>
          <p>A full audit trail</p>
        </div>
      </section>

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
