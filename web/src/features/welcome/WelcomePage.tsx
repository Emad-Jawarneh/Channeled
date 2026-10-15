import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { useAuth } from "../../auth/AuthContext";
import { createTeamWithGeneral } from "../teams/createTeam";
import {
  HierarchyChannelsIcon,
  HierarchyFilesIcon,
  TeamsIcon,
} from "../landing/FeatureIcons";
import { BrandLogo } from "../../brand/BrandLogo";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import styles from "./WelcomePage.module.css";

const steps = [
  { id: "teams", label: "Teams", Icon: TeamsIcon },
  { id: "channels", label: "Channels", Icon: HierarchyChannelsIcon },
  { id: "files", label: "Files", Icon: HierarchyFilesIcon },
] as const;

type Props = {
  loadingTeams?: boolean;
};

export function WelcomePage({ loadingTeams = false }: Props) {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function onSubmit(event: FormEvent) {
    event.preventDefault();
    if (name.trim().length < 2 || creating) return;
    setCreating(true);
    setError(null);
    try {
      const team = await createTeamWithGeneral(name);
      window.location.href = `/t/${team.slug}/c/general`;
    } catch (err) {
      setError(err instanceof Error ? err.message : "Could not create team.");
      setCreating(false);
    }
  }

  return (
    <div className={styles.page}>
      <section className={styles.hero} aria-label="Welcome">
        <div className={styles.heroTop}>
          <p className={styles.brandRow}>
            <BrandLogo decorative />
            Channeled
          </p>
          <ThemeToggle compact />
        </div>

        <div className={styles.heroCopy}>
          <p className={styles.eyebrow}>You are in</p>
          <h1 className={styles.heroTitle}>Welcome{user ? `, ${user.displayName}` : ""}</h1>
          <p className={styles.heroLede}>
            Teams are optional. Start in My files, or create a team whenever you are ready — you can create more later from the rail.
          </p>
        </div>

        <ol className={styles.flow} aria-label="How Channeled is organized">
          {steps.map((step, index) => (
            <li key={step.id} className={styles.flowItem}>
              {index > 0 && <span className={styles.flowArrow} aria-hidden="true" />}
              <article className={styles.flowCard}>
                <span className={styles.flowIcon}>
                  <step.Icon />
                </span>
                <span className={styles.flowLabel}>{step.label}</span>
              </article>
            </li>
          ))}
        </ol>
      </section>

      <main className={styles.panel}>
        {loadingTeams ? (
          <p className={styles.loading} data-testid="welcome-loading">
            Loading your workspace…
          </p>
        ) : (
          <form className={styles.form} onSubmit={(e) => void onSubmit(e)} data-testid="create-team-form">
            <p className={styles.kicker}>Optional</p>
            <h2>Create a team</h2>
            <p className={styles.hint}>
              A team holds channels. We open a <strong>general</strong> channel right away. Skip if you only need personal files for now.
            </p>

            <label className={styles.label}>
              Team name
              <input
                className={styles.input}
                name="team-display-name"
                data-testid="create-team-name"
                data-bwignore="true"
                data-1p-ignore="true"
                data-lpignore="true"
                value={name}
                onChange={(e) => setName(e.target.value)}
                placeholder="e.g. Product, Design, Ops"
                autoComplete="off"
                minLength={2}
                maxLength={80}
                autoFocus
              />
            </label>

            {error && (
              <p className={styles.error} role="alert">
                {error}
              </p>
            )}

            <div className={styles.formActions}>
              <Button
                type="submit"
                variant="primary"
                disabled={creating || name.trim().length < 2}
                data-testid="create-team"
              >
                {creating ? "Creating…" : "Create team"}
              </Button>
              <Link to="/me/files" className={styles.skip} data-testid="skip-create-team">
                Skip — go to My files
              </Link>
            </div>

            {user && (
              <p className={styles.footer}>
                Signed in as {user.email}.{" "}
                <button
                  type="button"
                  className={styles.signOut}
                  onClick={async () => {
                    await logout();
                    navigate("/login");
                  }}
                >
                  Sign out
                </button>
              </p>
            )}
          </form>
        )}
      </main>
    </div>
  );
}
