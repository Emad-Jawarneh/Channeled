import { useEffect, useState, type FormEvent } from "react";
import { useAuth } from "../../auth/AuthContext";
import { accountApi } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./Account.module.css";

export function ProfilePage() {
  const { user, refresh } = useAuth();
  const [displayName, setDisplayName] = useState("");
  const [username, setUsername] = useState("");
  const [bio, setBio] = useState("");
  const [usernameHint, setUsernameHint] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [ok, setOk] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  useEffect(() => {
    if (!user) return;
    setDisplayName(user.displayName);
    setUsername(user.username);
    setBio(user.bio ?? "");
  }, [user]);

  useEffect(() => {
    if (!user || !username || username === user.username) {
      setUsernameHint(null);
      return;
    }
    const handle = window.setTimeout(() => {
      void accountApi
        .usernameAvailable(username)
        .then((r) => setUsernameHint(r.available ? "Available." : (r.reason ?? "Unavailable.")))
        .catch(() => setUsernameHint(null));
    }, 300);
    return () => window.clearTimeout(handle);
  }, [username, user]);

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

  async function onDeleteAvatar() {
    setBusy(true);
    setError(null);
    try {
      await accountApi.deleteAvatar();
      await refresh();
      setOk("Avatar removed.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Remove failed");
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
        <div className={styles.avatarActions}>
          <label className={styles.label}>
            Avatar (JPEG/PNG/WebP, max 2 MB)
            <input
              type="file"
              accept="image/jpeg,image/png,image/webp"
              disabled={busy}
              onChange={(e) => void onAvatar(e.target.files?.[0] ?? null)}
            />
          </label>
          {avatarUrl && (
            <Button type="button" disabled={busy} onClick={() => void onDeleteAvatar()}>
              Remove avatar
            </Button>
          )}
        </div>
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
        {usernameHint && <p className={styles.hint}>{usernameHint}</p>}
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
