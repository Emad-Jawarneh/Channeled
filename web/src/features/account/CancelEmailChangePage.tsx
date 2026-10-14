import { useEffect, useState, type FormEvent } from "react";
import { accountApi } from "../../api/channeled";
import { takeFragmentToken } from "../../lib/fragmentToken";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

export function CancelEmailChangePage() {
  const [token, setToken] = useState("");
  const [manual, setManual] = useState("");
  const [password, setPassword] = useState("");
  const [msg, setMsg] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    setToken(takeFragmentToken());
  }, []);

  const value = () => token || manual;

  async function cancel() {
    setError(null);
    try {
      await accountApi.cancelEmailChange(value());
      setMsg("Pending email change cancelled.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed");
    }
  }

  async function signOutAll() {
    setError(null);
    try {
      await accountApi.signOutAllEmailChange(value());
      setMsg("Signed out everywhere.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed");
    }
  }

  async function changePassword(e: FormEvent) {
    e.preventDefault();
    setError(null);
    try {
      await accountApi.passwordFromCancelToken(value(), password);
      setMsg("Password changed. All other sessions ended.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed");
    }
  }

  return (
    <main className={styles.page}>
      <div className={styles.form}>
        <h1 style={{ margin: 0 }}>This wasn't me</h1>
        <p>Cancel the email change and optionally secure the account.</p>
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
        <Button type="button" variant="primary" onClick={() => void cancel()}>
          Cancel email change
        </Button>
        <Button type="button" onClick={() => void signOutAll()}>
          Sign out everywhere
        </Button>
        <form onSubmit={changePassword}>
          <label className={styles.label}>
            New password
            <input
              className={styles.input}
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              minLength={12}
              required
            />
          </label>
          <Button type="submit">Change password</Button>
        </form>
        {error && <p className={styles.error}>{error}</p>}
        {msg && <p>{msg}</p>}
      </div>
    </main>
  );
}
