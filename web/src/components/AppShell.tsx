import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, NavLink, useNavigate, useParams } from "react-router-dom";
import { useState, type ReactNode } from "react";
import { useAuth } from "../auth/AuthContext";
import { teamsApi } from "../api/channeled";
import { CreateTeamDialog } from "../features/teams/CreateTeamDialog";
import { createTeamWithGeneral } from "../features/teams/createTeam";
import { UploadTray } from "../uploads/UploadTray";
import { ThemeToggle } from "../theme/ThemeToggle";
import { Button } from "../ui/Button";
import { NotificationBell } from "./NotificationBell";
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
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const teams = useQuery({
    queryKey: ["teams"],
    queryFn: () => teamsApi.list(),
    enabled: !!user,
  });

  async function onCreateTeam(name: string) {
    setCreating(true);
    setCreateError(null);
    try {
      const team = await createTeamWithGeneral(name);
      await queryClient.invalidateQueries({ queryKey: ["teams"] });
      setCreateOpen(false);
      navigate(`/t/${team.slug}/c/general`);
    } catch (err) {
      setCreateError(err instanceof Error ? err.message : "Could not create team.");
    } finally {
      setCreating(false);
    }
  }

  return (
    <div className={styles.shell} data-has-details={details ? "true" : "false"}>
      <aside className={styles.rail} aria-label="Team rail">
        <Link to="/" className={styles.brand} data-testid="brand">
          C
        </Link>
        <nav className={styles.railNav} aria-label="Teams">
          <NavLink to="/me/files" className={`${styles.railItem} ${styles.railMe}`} title="My files" data-testid="rail-me">
            Me
          </NavLink>
          <div className={styles.railDivider} aria-hidden="true" />
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
          <button
            type="button"
            className={`${styles.railItem} ${styles.railAdd}`}
            title="Create team"
            aria-label="Create team"
            data-testid="rail-create-team"
            onClick={() => {
              setCreateError(null);
              setCreateOpen(true);
            }}
          >
            +
          </button>
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
          {user && (
            <span className={styles.railItem} title={user.displayName} aria-hidden="true">
              {user.displayName.slice(0, 2).toUpperCase()}
            </span>
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
          <h1 className={styles.pageTitle}>
            <strong>{title ?? "Channeled"}</strong>
          </h1>
          <div className={styles.topActions}>
            <ThemeToggle compact />
            <NotificationBell />
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
        <div className={styles.content}>{children}</div>
      </main>

      {details && (
        <aside className={styles.details} aria-label="Details">
          {details}
        </aside>
      )}
      <UploadTray />
      <CreateTeamDialog
        open={createOpen}
        busy={creating}
        error={createError}
        onClose={() => {
          if (!creating) setCreateOpen(false);
        }}
        onCreate={onCreateTeam}
      />
    </div>
  );
}
