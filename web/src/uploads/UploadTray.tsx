import { useEffect, useRef, useState } from "react";
import { formatBytes } from "../lib/formatBytes";
import { useUploads } from "./UploadContext";
import styles from "./UploadTray.module.css";

export function UploadTray() {
  const { jobs, pause, resume, retry, clearFinished } = useUploads();
  const [dismissed, setDismissed] = useState(false);
  const knownIds = useRef(new Set<string>());

  useEffect(() => {
    let added = false;
    for (const job of jobs) {
      if (!knownIds.current.has(job.id)) {
        knownIds.current.add(job.id);
        added = true;
      }
    }
    if (added) setDismissed(false);
  }, [jobs]);

  if (jobs.length === 0 || dismissed) {
    return null;
  }

  return (
    <section className={styles.tray} aria-label="Upload queue">
      <div className={styles.header}>
        <h2 className={styles.title}>Uploads</h2>
        <button
          type="button"
          className={styles.close}
          aria-label="Close uploads"
          data-testid="upload-tray-close"
          onClick={() => {
            clearFinished();
            setDismissed(true);
          }}
        >
          <span aria-hidden="true">×</span>
        </button>
      </div>
      <ul className={styles.list}>
        {jobs.slice(0, 8).map((job) => (
          <li key={job.id} className={styles.item}>
            <div>
              <strong>{job.fileName}</strong>
              <span className={styles.meta}>
                {job.status} · {formatBytes(job.sizeBytes)} · {job.progress}%
              </span>
              {job.error && <span className={styles.error}>{job.error}</span>}
            </div>
            <div className={styles.actions}>
              {job.status === "uploading" && (
                <button type="button" onClick={() => pause(job.id)}>
                  Pause
                </button>
              )}
              {job.status === "paused" && (
                <button type="button" onClick={() => resume(job.id)}>
                  Resume
                </button>
              )}
              {job.status === "error" && (
                <button type="button" onClick={() => retry(job.id)}>
                  Retry
                </button>
              )}
            </div>
            <progress max={100} value={job.progress} aria-label={`${job.fileName} progress`} />
          </li>
        ))}
      </ul>
    </section>
  );
}
