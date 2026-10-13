import { useQuery } from "@tanstack/react-query";
import { useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { publicShareApi } from "../../api/channeled";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import login from "../login/LoginPage.module.css";
import styles from "./SharePage.module.css";

function formatBytes(n: number): string {
  if (n < 1024) return `${n} bytes`;
  const units = ["KB", "MB", "GB", "TB"];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) {
    v /= 1024;
    i += 1;
  }
  return `${v.toFixed(1)} ${units[i]}`;
}

/**
 * Public, anonymous download page for /s/:token. No session or CSRF is involved. Clicking "Download" claims
 * (consumes) one download and then navigates to the short-lived ticketed URL. The idempotency key is kept for
 * the lifetime of the page so that a retry/double-click does not burn a second download.
 */
export function SharePage() {
  const { token = "" } = useParams();
  const idempotencyKey = useRef(crypto.randomUUID());
  const [claiming, setClaiming] = useState(false);
  const [claimError, setClaimError] = useState<string | null>(null);
  const [started, setStarted] = useState(false);

  const info = useQuery({
    queryKey: ["public-share", token],
    queryFn: () => publicShareApi.info(token),
    retry: false,
  });

  async function onDownload() {
    setClaiming(true);
    setClaimError(null);
    try {
      const claim = await publicShareApi.claim(token, idempotencyKey.current);
      setStarted(true);
      window.location.assign(claim.downloadPath);
    } catch (e) {
      setClaimError(e instanceof Error ? e.message : "Could not start the download.");
    } finally {
      setClaiming(false);
    }
  }

  return (
    <main className={`${login.page} ${login.pageCentered}`}>
      <div className={`${login.form} ${styles.stack}`} data-testid="share-page">
        <div className={login.heroTop}>
          <h1 className={styles.title}>Shared file</h1>
          <ThemeToggle compact />
        </div>

        {info.isLoading && <p className={login.hint}>Loading…</p>}

        {info.isError && (
          <p className={login.error} data-testid="share-unavailable">
            This link is not available. It may have expired, been revoked, or reached its download limit.
          </p>
        )}

        {info.data && (
          <>
            <h2 className={styles.fileName} data-testid="share-file-name">
              {info.data.fileName}
            </h2>
            <p className={styles.meta}>
              {formatBytes(info.data.sizeBytes)}
              {info.data.remainingDownloads != null && <> · {info.data.remainingDownloads} downloads left</>}
              <br />
              Link expires {new Date(info.data.expiresAt).toLocaleString()}
            </p>
            {claimError && (
              <p className={login.error} data-testid="share-claim-error">
                {claimError}
              </p>
            )}
            {started && (
              <p className={styles.started} data-testid="share-started">
                Your download is starting…
              </p>
            )}
            <Button
              type="button"
              variant="primary"
              disabled={claiming}
              data-testid="share-download"
              onClick={() => void onDownload()}
            >
              {claiming ? "Preparing…" : "Download"}
            </Button>
          </>
        )}
      </div>
    </main>
  );
}
