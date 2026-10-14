import { useEffect, useState, type FormEvent } from "react";
import { accountApi } from "../../api/channeled";
import { takeFragmentToken } from "../../lib/fragmentToken";
import { Button } from "../../ui/Button";
import styles from "../login/LoginPage.module.css";

export function ConfirmEmailPage() {
  const [token, setToken] = useState("");
  const [manual, setManual] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [done, setDone] = useState(false);

  useEffect(() => {
    setToken(takeFragmentToken());
  }, []);

  async function onSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    const value = token || manual;
    if (!value) {
      setError("Paste the confirmation token from your email.");
      return;
    }
    try {
      await accountApi.confirmEmailChange(value);
      setDone(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : "Confirm failed");
    }
  }

  return (
    <main className={styles.page}>
      <form className={styles.form} onSubmit={onSubmit}>
        <h1 style={{ margin: 0 }}>Confirm email change</h1>
        <p>
          This page does not change your email until you press Confirm.
        </p>
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
        {error && <p className={styles.error}>{error}</p>}
        {done ? (
          <p>Email updated. You can sign in with the new address.</p>
        ) : (
          <Button type="submit" variant="primary">
            Confirm
          </Button>
        )}
      </form>
    </main>
  );
}
