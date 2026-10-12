import { useState, type FormEvent } from "react";
import { useNavigate } from "react-router-dom";
import { Button } from "../../ui/Button";
import styles from "./LoginPage.module.css";

async function fetchCsrf(): Promise<string> {
  const response = await fetch("/api/v1/auth/csrf", { credentials: "same-origin" });
  if (!response.ok) {
    throw new Error("Failed to fetch CSRF token");
  }
  const body = (await response.json()) as { token: string };
  return body.token;
}

export function LoginPage() {
  const navigate = useNavigate();
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    setLoading(true);
    setError(null);

    try {
      // Login is CSRF-exempt for cookie issuance; subsequent mutations need CSRF.
      const response = await fetch("/api/v1/auth/login", {
        method: "POST",
        credentials: "same-origin",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ email, password }),
      });

      if (!response.ok) {
        const problem = (await response.json().catch(() => null)) as { detail?: string } | null;
        setError(problem?.detail ?? "Sign in failed.");
        return;
      }

      await fetchCsrf();
      navigate("/");
    } catch {
      setError("Unable to reach API. Is it running?");
    } finally {
      setLoading(false);
    }
  }

  return (
    <main className={styles.page}>
      <form className={styles.form} onSubmit={onSubmit}>
        <h1>Channeled</h1>
        <p className={styles.hint}>Sign in with your invitation account.</p>
        <label className={styles.label}>
          Email
          <input
            className={styles.input}
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
            type="password"
            autoComplete="current-password"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            required
            minLength={12}
          />
        </label>
        {error && <p className={styles.error}>{error}</p>}
        <Button type="submit" variant="primary" disabled={loading}>
          {loading ? "Signing in…" : "Sign in"}
        </Button>
      </form>
    </main>
  );
}
