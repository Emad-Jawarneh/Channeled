import { useEffect, useState, type FormEvent } from "react";
import { accountApi, authApi } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./Account.module.css";

export function SecurityPage() {
  const [currentPassword, setCurrent] = useState("");
  const [newPassword, setNew] = useState("");
  const [newEmail, setNewEmail] = useState("");
  const [emailPassword, setEmailPassword] = useState("");
  const [mailOk, setMailOk] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState<string | null>(null);

  useEffect(() => {
    void accountApi.selfServiceMail().then((r) => setMailOk(r.available)).catch(() => setMailOk(false));
  }, []);

  async function onPassword(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setOk(null);
    try {
      await authApi.changePassword(currentPassword, newPassword);
      setCurrent("");
      setNew("");
      setOk("Password updated. Other sessions were signed out.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed");
    }
  }

  async function onEmail(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setOk(null);
    try {
      await accountApi.startEmailChange(newEmail, emailPassword);
      setOk("Check both email addresses to confirm or cancel the change.");
      setNewEmail("");
      setEmailPassword("");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Failed");
    }
  }

  return (
    <>
      <h2>Security</h2>
      <div className={styles.securityReserve} aria-hidden />
      <form className={styles.form} onSubmit={onPassword}>
        <h3>Change password</h3>
        <label className={styles.label}>
          Current password
          <input type="password" value={currentPassword} onChange={(e) => setCurrent(e.target.value)} required />
        </label>
        <label className={styles.label}>
          New password
          <input type="password" value={newPassword} onChange={(e) => setNew(e.target.value)} required minLength={12} />
        </label>
        <Button type="submit" variant="primary">
          Update password
        </Button>
      </form>
      {mailOk ? (
        <form className={styles.form} onSubmit={onEmail}>
          <h3>Change email</h3>
          <p className={styles.hint}>Your current email stays the login until you confirm the new one.</p>
          <label className={styles.label}>
            New email
            <input type="email" value={newEmail} onChange={(e) => setNewEmail(e.target.value)} required />
          </label>
          <label className={styles.label}>
            Current password
            <input type="password" value={emailPassword} onChange={(e) => setEmailPassword(e.target.value)} required />
          </label>
          <Button type="submit">Send confirmation</Button>
        </form>
      ) : (
        <p className={styles.hint}>Email change is unavailable until the operator verifies SMTP.</p>
      )}
      {error && <p className={styles.error}>{error}</p>}
      {ok && <p className={styles.ok}>{ok}</p>}
    </>
  );
}
