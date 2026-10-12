import { useUploads } from "./UploadContext";
import styles from "./UploadTray.module.css";

export function UploadTray() {
  const { jobs, pause, resume, retry } = useUploads();
  if (jobs.length === 0) {
    return null;
  }

  return (
    <section className={styles.tray} aria-label="Upload queue">
      <h2 className={styles.title}>Uploads</h2>
      <ul className={styles.list}>
        {jobs.slice(0, 8).map((job) => (
          <li key={job.id} className={styles.item}>
            <div>
              <strong>{job.fileName}</strong>
              <span className={styles.meta}>
                {job.status} · {job.progress}%
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
