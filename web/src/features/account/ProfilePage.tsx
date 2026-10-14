import { useEffect, useRef, useState, type FormEvent } from "react";
import { useAuth } from "../../auth/AuthContext";
import { accountApi } from "../../api/channeled";
import { Button } from "../../ui/Button";
import styles from "./Account.module.css";

export function ProfilePage() {
  const { user, refresh } = useAuth();
  const avatarInputRef = useRef<HTMLInputElement>(null);
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
    setOk(null);
    try {
      await accountApi.uploadAvatar(file);
      await refresh();
      setOk("Avatar updated.");
    } catch (err) {
      setError(err instanceof Error ? err.message : "Upload failed");
    } finally {
      setBusy(false);
      if (avatarInputRef.current) avatarInputRef.current.value = "";
    }
  }

  async function onDeleteAvatar() {
    setBusy(true);
    setError(null);
    setOk(null);
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

  const nameDirty = displayName !== user.displayName;
  const usernameDirty = username !== user.username;
  const bioDirty = bio !== (user.bio ?? "");
  const dirty = nameDirty || usernameDirty || bioDirty;

  return (
    <div className={styles.page}>
      <header className={styles.pageHeader}>
        <p className={styles.eyebrow}>Account</p>
        <h1 className={styles.pageTitle}>Profile</h1>
        <p className={styles.pageLede}>
          Update how you appear to teammates: avatar, display name, username, and bio.
        </p>
      </header>

      {(error || ok) && (
        <p className={error ? styles.error : styles.ok} role={error ? "alert" : "status"}>
          {error ?? ok}
        </p>
      )}

      <section className={styles.card} data-testid="account-profile-card">
        <h2 className={styles.cardTitle}>Your details</h2>
        <p className={styles.cardIntro}>
          Your avatar appears in chat, member lists, and the account menu. Square images work best.
        </p>
        <div className={styles.profile}>
          <div className={styles.logoBlock}>
            {avatarUrl ? (
              <img className={styles.avatar} src={avatarUrl} alt="" />
            ) : (
              <div className={styles.avatar} aria-hidden />
            )}
            <input
              ref={avatarInputRef}
              type="file"
              accept="image/jpeg,image/png,image/webp"
              hidden
              data-testid="account-avatar-input"
              onChange={(e) => void onAvatar(e.target.files?.[0] ?? null)}
            />
            <div className={styles.logoActions}>
              <Button
                type="button"
                data-testid="account-avatar-upload"
                disabled={busy}
                onClick={() => avatarInputRef.current?.click()}
              >
                {busy ? "Working…" : avatarUrl ? "Replace avatar" : "Upload avatar"}
              </Button>
              {avatarUrl && (
                <Button
                  type="button"
                  data-testid="account-avatar-remove"
                  disabled={busy}
                  onClick={() => void onDeleteAvatar()}
                >
                  Remove
                </Button>
              )}
            </div>
            <p className={styles.logoHint}>JPEG, PNG, or WebP · max 2 MB</p>
          </div>

          <form className={styles.profileFields} onSubmit={onSave}>
            <label className={styles.field}>
              Display name
              <input
                value={displayName}
                onChange={(e) => setDisplayName(e.target.value)}
                required
                disabled={busy}
                data-testid="account-display-name"
              />
            </label>
            <label className={styles.field}>
              Username
              <input
                value={username}
                onChange={(e) => setUsername(e.target.value)}
                required
                disabled={busy}
                data-testid="account-username"
              />
            </label>
            <p className={styles.muted}>3–30 characters; letters, digits, dots, underscores, hyphens.</p>
            {usernameHint && <p className={styles.muted}>{usernameHint}</p>}
            <label className={styles.field}>
              Bio
              <textarea
                value={bio}
                onChange={(e) => setBio(e.target.value)}
                maxLength={280}
                rows={4}
                placeholder="A short intro (optional)"
                disabled={busy}
                data-testid="account-bio"
              />
            </label>
            <p className={styles.muted}>{bio.length}/280</p>
            <div className={styles.actions}>
              <Button
                type="submit"
                variant="primary"
                disabled={busy || !dirty || !displayName.trim() || !username.trim()}
                data-testid="account-profile-save"
              >
                Save details
              </Button>
            </div>
          </form>
        </div>
      </section>
    </div>
  );
}
