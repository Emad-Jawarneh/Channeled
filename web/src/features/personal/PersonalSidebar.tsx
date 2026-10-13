import { NavLink } from "react-router-dom";
import styles from "../files/FileBrowser.module.css";

function TrashIcon() {
  return (
    <svg
      className={styles.sideLinkIcon}
      viewBox="0 0 24 24"
      width="16"
      height="16"
      fill="none"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M3 6h18" />
      <path d="M8 6V4h8v2" />
      <path d="M19 6l-1 14H6L5 6" />
      <path d="M10 11v6" />
      <path d="M14 11v6" />
    </svg>
  );
}

export function PersonalSidebar() {
  return (
    <div className={styles.sideRoot}>
      <div className={styles.sideHead}>
        <div className={styles.sideMark}>M</div>
        <div>
          <p className={styles.sideTitle}>My files</p>
          <p className={styles.sideMeta}>Personal space</p>
        </div>
      </div>

      <div className={styles.sideScroll}>
        <p className={`${styles.sideLabel} ${styles.sideLabelFirst}`}>LIBRARY</p>
        <nav className={styles.channelNav} aria-label="My files">
          <NavLink
            to="/me/files"
            data-testid="me-files"
            end
            className={({ isActive }) => (isActive ? styles.active : undefined)}
          >
            All files
          </NavLink>
          <NavLink
            to="/me/files/recent"
            className={({ isActive }) => (isActive ? styles.active : undefined)}
          >
            Recent
          </NavLink>
          <NavLink
            to="/me/files/starred"
            className={({ isActive }) => (isActive ? styles.active : undefined)}
          >
            Starred
          </NavLink>
        </nav>
      </div>

      <div className={styles.sideFoot}>
        <nav className={styles.sideLinks} aria-label="Personal tools">
          <NavLink
            to="/me/files/trash"
            data-testid="me-trash"
            className={({ isActive }) => (isActive ? styles.active : undefined)}
          >
            <TrashIcon />
            Trash
          </NavLink>
        </nav>
      </div>
    </div>
  );
}
