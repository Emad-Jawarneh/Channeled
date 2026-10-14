import { useState, type FormEvent } from "react";
import { useAuth } from "../../auth/AuthContext";
import { accountApi } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./Account.module.css";

export function ProfilePage() {
  const { user, refresh } = useAuth();
  const [displayName, setDisplayName] = useState(user?.displayName ?? "");
  const [username, setUsername] = useState(user?.username ?? "");
  const [bio, setBio] = useState(user?.bio ?? "");
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  if (!user) return null;

  async function onSave(e: FormEvent) {
    e.preventDefault();
    setBusy(true);
    setError(null);
    setOk(null);
    try {
      await accountApi.updateProfile({ displayName, username, bio });
      await refresh();
      setOk("Saved.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Save failed");
    } finally {
      setBusy(false);
    }
  }

  async function onAvatar(file: File | null) {
    if (!file) return;
    setBusy(true);
    setError(null);
    try {
      await accountApi.uploadAvatar(file);
      await refresh();
      setOk("Avatar updated.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setBusy(false);
    }
  }

  const avatarUrl = user.avatarVersion
    ? `/api/v1/users/${user.id}/avatar?v=${user.avatarVersion}`
    : null;

  return (
    <>
      <h2>Profile</h2>
      <div className={styles.avatarRow}>
        {avatarUrl ? (
          <img className={styles.avatar} src={avatarUrl} alt="" />
        ) : (
          <div className={styles.avatar} aria-hidden />
        )}
        <label className={styles.label}>
          Avatar (JPEG/PNG/WebP, max 2 MB)
          <input
            type="file"
            accept="image/jpeg,image/png,image/webp"
            disabled={busy}
            onChange={(e) => void onAvatar(e.target.files?.[0] ?? null)}
          />
        </label>
      </div>
      <form className={styles.form} onSubmit={onSave}>
        <label className={styles.label}>
          Display name
          <input value={displayName} onChange={(e) => setDisplayName(e.target.value)} required />
        </label>
        <label className={styles.label}>
          Username
          <input value={username} onChange={(e) => setUsername(e.target.value)} required />
        </label>
        <p className={styles.hint}>3–30 characters; letters, digits, dots, underscores, hyphens.</p>
        <label className={styles.label}>
          Bio
          <textarea
            className={styles.bio}
            value={bio}
            onChange={(e) => setBio(e.target.value)}
            maxLength={280}
            rows={4}
            placeholder="A short intro (optional)"
          />
        </label>
        <p className={styles.hint}>{bio.length}/280</p>
        {error && <p className={styles.error}>{error}</p>}
        {ok && <p className={styles.ok}>{ok}</p>}
        <Button type="submit" variant="primary" disabled={busy}>
          Save
        </Button>
      </form>
    </>
  );
}
