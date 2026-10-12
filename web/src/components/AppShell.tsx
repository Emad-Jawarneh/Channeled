import { useQuery } from "@tanstack/react-query";
import { Link, NavLink, useNavigate, useParams } from "react-router-dom";
import type { ReactNode } from "react";
import { useAuth } from "../auth/AuthContext";
import { teamsApi } from "../api/channeled";
import { UploadTray } from "../uploads/UploadTray";
import { Button } from "../ui/Button";
import styles from "./AppShell.module.css";

type AppShellProps = {
  children: ReactNode;
  sidebar?: ReactNode;
  details?: ReactNode;
  title?: string;
};

export function AppShell({ children, sidebar, details, title }: AppShellProps) {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const params = useParams();
  const teams = useQuery({
    queryKey: ["teams"],
    queryFn: () => teamsApi.list(),
    enabled: !!user,
  });

  return (
    <div className={styles.shell} data-has-details={details ? "true" : "false"}>
      <aside className={styles.rail} aria-label="Team rail">
        <Link to="/me/files" className={styles.brand} data-testid="brand">
          C
        </Link>
        <nav className={styles.railNav} aria-label="Teams">
          <NavLink to="/me/files" className={styles.railItem} title="My files" data-testid="rail-me">
            Me
          </NavLink>
          {(teams.data ?? []).map((team) => (
            <NavLink
              key={team.id}
              to={`/t/${team.slug}/c/general`}
              className={styles.railItem}
              title={team.name}
              data-testid={`rail-team-${team.slug}`}
            >
              {team.name.slice(0, 2).toUpperCase()}
            </NavLink>
          ))}
        </nav>
        <div className={styles.railFooter}>
          <NavLink to="/search" className={styles.railItem} title="Search">
            ⌕
          </NavLink>
          {user?.isPlatformAdmin && (
            <NavLink to="/admin/dashboard" className={styles.railItem} title="Admin" data-testid="rail-admin">
              Ad
            </NavLink>
          )}
        </div>
      </aside>

      <aside className={styles.sidebar} aria-label="Sidebar">
        {sidebar ?? (
          <div>
            <p className={styles.sidebarTitle}>{title ?? "Channeled"}</p>
            {params.team && (
              <nav className={styles.sideNav}>
                <NavLink to={`/t/${params.team}/settings/members`}>Members</NavLink>
                <NavLink to={`/t/${params.team}/settings/channels`}>Channels</NavLink>
                <NavLink to={`/t/${params.team}/settings/storage`}>Storage</NavLink>
              </nav>
            )}
          </div>
        )}
      </aside>

      <main className={styles.main}>
        <header className={styles.topbar}>
          <h1 className={styles.pageTitle}>{title ?? "Channeled"}</h1>
          <div className={styles.topActions}>
            {user && (
              <span className={styles.user} data-testid="current-user">
                {user.displayName}
              </span>
            )}
            {user && (
              <Button
                type="button"
                onClick={async () => {
                  await logout();
                  navigate("/login");
                }}
              >
                Sign out
              </Button>
            )}
          </div>
        </header>
        {children}
      </main>

      {details && (
        <aside className={styles.details} aria-label="Details">
          {details}
        </aside>
      )}
      <UploadTray />
    </div>
  );
}
