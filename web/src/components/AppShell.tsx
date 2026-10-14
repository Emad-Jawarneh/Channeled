import { useQuery, useQueryClient } from "@tanstack/react-query";
import { Link, NavLink, useLocation, useNavigate, useParams } from "react-router-dom";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { useAuth } from "../auth/AuthContext";
import { teamsApi } from "../api/channeled";
import { CreateTeamDialog } from "../features/teams/CreateTeamDialog";
import { createTeamWithGeneral } from "../features/teams/createTeam";
import { TeamMark } from "../features/teams/TeamMark";
import { UploadTray } from "../uploads/UploadTray";
import { ThemeToggle } from "../theme/ThemeToggle";
import { ClipboardShelf } from "../features/files/ClipboardShelf";
import { NotificationBell } from "./NotificationBell";
import styles from "./AppShell.module.css";

type AppShellProps = {
  children: ReactNode;
  sidebar?: ReactNode;
  details?: ReactNode;
  title?: string;
  /** Placed in the top bar before the theme toggle (e.g. files search). */
  topbarExtra?: ReactNode;
};

function pathUnder(pathname: string, prefix: string): boolean {
  return pathname === prefix || pathname.startsWith(`${prefix}/`);
}

export function AppShell({ children, sidebar, details, title, topbarExtra }: AppShellProps) {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const params = useParams();
  const queryClient = useQueryClient();
  const [createOpen, setCreateOpen] = useState(false);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState<string | null>(null);
  const [accountOpen, setAccountOpen] = useState(false);
  const accountMenuRef = useRef<HTMLDivElement>(null);
  const teams = useQuery({
    queryKey: ["teams"],
    queryFn: () => teamsApi.list(),
    enabled: !!user,
  });

  useEffect(() => {
    if (!accountOpen) return;
    function onPointerDown(event: MouseEvent) {
      if (!accountMenuRef.current?.contains(event.target as Node)) {
        setAccountOpen(false);
      }
    }
    function onKeyDown(event: KeyboardEvent) {
      if (event.key === "Escape") setAccountOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [accountOpen]);

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
          <NavLink
            to="/me/files"
            className={() =>
              [
                styles.railItem,
                styles.railMe,
                pathUnder(location.pathname, "/me") ? styles.railItemActive : "",
              ]
                .filter(Boolean)
                .join(" ")
            }
            aria-current={pathUnder(location.pathname, "/me") ? "page" : undefined}
            title="My files"
            data-testid="rail-me"
          >
            Me
          </NavLink>
          <div className={styles.railDivider} aria-hidden="true" />
          {(teams.data ?? []).map((team) => {
            const active = pathUnder(location.pathname, `/t/${team.slug}`);
            return (
              <NavLink
                key={team.id}
                to={`/t/${team.slug}/c/general`}
                className={() =>
                  [
                    styles.railItem,
                    team.hasLogo ? styles.railItemLogo : "",
                    active ? styles.railItemActive : "",
                  ]
                    .filter(Boolean)
                    .join(" ")
                }
                aria-current={active ? "page" : undefined}
                title={team.name}
                data-testid={`rail-team-${team.slug}`}
              >
                <TeamMark team={team} size="md" className={styles.railMark} />
              </NavLink>
            );
          })}
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
          <NavLink
            to="/search"
            className={() =>
              [styles.railItem, pathUnder(location.pathname, "/search") ? styles.railItemActive : ""]
                .filter(Boolean)
                .join(" ")
            }
            aria-current={pathUnder(location.pathname, "/search") ? "page" : undefined}
            title="Search"
          >
            ⌕
          </NavLink>
          {user?.isPlatformAdmin && (
            <NavLink
              to="/admin/dashboard"
              className={() =>
                [
                  styles.railItem,
                  pathUnder(location.pathname, "/admin") ? styles.railItemActive : "",
                ]
                  .filter(Boolean)
                  .join(" ")
              }
              aria-current={pathUnder(location.pathname, "/admin") ? "page" : undefined}
              title="Admin"
              data-testid="rail-admin"
            >
              Ad
            </NavLink>
          )}
          {user && (
            <div className={styles.accountMenu} ref={accountMenuRef}>
              <button
                type="button"
                className={`${styles.railItem} ${styles.accountTrigger}`}
                data-testid="current-user"
                title={user.displayName}
                aria-label={`Account menu for ${user.displayName}`}
                aria-expanded={accountOpen}
                aria-haspopup="menu"
                onClick={() => setAccountOpen((v) => !v)}
              >
                {user.avatarVersion ? (
                  <img
                    className={styles.accountAvatar}
                    src={`/api/v1/users/${user.id}/avatar?v=${user.avatarVersion}`}
                    alt=""
                  />
                ) : (
                  user.displayName.slice(0, 2).toUpperCase()
                )}
              </button>
              {accountOpen && (
                <div className={styles.accountDropdown} role="menu">
                  <Link role="menuitem" to="/account/profile" onClick={() => setAccountOpen(false)}>
                    Profile
                  </Link>
                  <Link role="menuitem" to="/account/security" onClick={() => setAccountOpen(false)}>
                    Security
                  </Link>
                  <Link role="menuitem" to="/account/sessions" onClick={() => setAccountOpen(false)}>
                    Sessions
                  </Link>
                  <button
                    type="button"
                    role="menuitem"
                    onClick={async () => {
                      setAccountOpen(false);
                      await logout();
                      navigate("/login");
                    }}
                  >
                    Sign out
                  </button>
                </div>
              )}
            </div>
          )}
        </div>
      </aside>

      <aside className={styles.sidebar} aria-label="Sidebar">
        {sidebar ?? (
          <div className={styles.settingsSide}>
            <p className={styles.sidebarTitle}>{title ?? "Channeled"}</p>
            {params.team && (
              <>
                <p className={styles.settingsLabel}>Settings</p>
                <nav className={styles.sideNav} aria-label="Team settings">
                  <NavLink to={`/t/${params.team}/settings/general`}>General</NavLink>
                  <NavLink to={`/t/${params.team}/settings/members`}>Members</NavLink>
                  <NavLink to={`/t/${params.team}/settings/channels`}>Channels</NavLink>
                  <NavLink to={`/t/${params.team}/settings/storage`}>Storage</NavLink>
                </nav>
              </>
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
            {topbarExtra}
            <ThemeToggle compact />
            <ClipboardShelf />
            <NotificationBell />
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
