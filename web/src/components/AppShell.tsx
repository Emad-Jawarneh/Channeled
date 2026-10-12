import type { ReactNode } from "react";
import styles from "./AppShell.module.css";

type AppShellProps = {
  children: ReactNode;
};

export function AppShell({ children }: AppShellProps) {
  return (
    <div className={styles.shell}>
      <aside className={styles.rail} aria-label="Team rail">
        <p className={styles.brand}>Channeled</p>
        <nav>
          <div>Me</div>
        </nav>
      </aside>
      <aside className={styles.sidebar} aria-label="Sidebar">
        <p>Channels</p>
      </aside>
      <main className={styles.main}>{children}</main>
    </div>
  );
}
