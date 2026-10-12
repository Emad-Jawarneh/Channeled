import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

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
          <h1 className={styles.heroTitle}>Create your account and start sharing.</h1>
          <p className={styles.heroLede}>
            Self-hosted file sharing for teams — channels, personal space, and audit you control.
          </p>
        </div>
        <div className={styles.heroPoints}>
          <p>Independent teams and channels</p>
          <p>Private, self-hosted storage</p>
          <p>A full audit trail</p>
        </div>
      </section>

      <main className={styles.panel}>
        <form className={styles.form} onSubmit={(e) => void onSubmit(e)} data-testid="signup-form">
          <h2>Sign up</h2>
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
          <p className={styles.footer}>
            Already have an account? <Link to="/login">Log in</Link>
          </p>
        </form>
      </main>
    </div>
  );
}
