import { Link } from "react-router-dom";
import { ThemeToggle } from "../../theme/ThemeToggle";
import {
  AuditIcon,
  ChannelsIcon,
  FilesIcon,
  HierarchyChannelsIcon,
  HierarchyFilesIcon,
  HostingIcon,
  PermissionsIcon,
  PersonalIcon,
  TeamsIcon,
} from "./FeatureIcons";
import styles from "./LandingPage.module.css";

const hierarchy = [
  { id: "teams", label: "Teams", Icon: TeamsIcon },
  { id: "channels", label: "Channels", Icon: HierarchyChannelsIcon },
  { id: "files", label: "Files", Icon: HierarchyFilesIcon },
] as const;

const features = [
  {
    id: "channels",
    title: "Teams and channels",
    body: "Organize work by team, then by channel. Open channels for everyone on the team; private channels for the people who need them.",
    Icon: ChannelsIcon,
  },
  {
    id: "personal",
    title: "Personal space",
    body: "Keep drafts and working files in My files — private to you — then copy into a team channel when you are ready to share.",
    Icon: PersonalIcon,
  },
  {
    id: "permissions",
    title: "Clear permissions",
    body: "Owners, admins, members, and viewers map to real actions. Hidden resources stay hidden — including from people who should not know they exist.",
    Icon: PermissionsIcon,
  },
  {
    id: "files",
    title: "Uploads you can trust",
    body: "Resumable uploads, folders, trash with restore, search across channel and personal space, and quotas you can see before you hit the wall.",
    Icon: FilesIcon,
  },
  {
    id: "audit",
    title: "Audit and admin",
    body: "Every important action is recorded. Platform admins manage users, teams, storage health, and backups from one place.",
    Icon: AuditIcon,
  },
  {
    id: "hosting",
    title: "Self-hosted by design",
    body: "Your files stay on your infrastructure. PostgreSQL for metadata, S3-compatible object storage for bytes — no SaaS lock-in.",
    Icon: HostingIcon,
  },
] as const;

export function LandingPage() {
  return (
    <div className={styles.page}>
      <header className={styles.top}>
        <div className={styles.shell}>
          <p className={styles.brandMark} data-testid="landing-brand">
            Channeled
          </p>
          <nav className={styles.topNav} aria-label="Account">
            <ThemeToggle compact />
            <Link to="/login" className={styles.navLink} data-testid="landing-login-nav">
              Log in
            </Link>
            <Link to="/signup" className={styles.navCta} data-testid="landing-signup-nav">
              Sign up
            </Link>
          </nav>
        </div>
      </header>

      <section className={styles.hero} aria-label="Introduction">
        <div className={styles.shell}>
          <div className={styles.heroCopy}>
            <h1 className={styles.brand}>Channeled</h1>
            <p className={styles.lede}>
              Self-hosted file sharing for teams — channels, personal space, and audit you control.
            </p>
            <div className={styles.actions}>
              <Link to="/signup" className={styles.primary} data-testid="landing-signup">
                Sign up
              </Link>
              <Link to="/login" className={styles.secondary} data-testid="landing-login">
                Log in
              </Link>
            </div>
          </div>

          <div className={styles.heroVisual} aria-hidden="true">
            <div className={styles.preview}>
              <div className={styles.previewTop}>
                <span className={styles.previewDot} />
                <span className={styles.previewDot} />
                <span className={styles.previewDot} />
                <span className={styles.previewPath}>Acme Design / # brand-assets</span>
              </div>
              <div className={styles.previewBody}>
                <div className={styles.previewSide}>
                  <span className={styles.previewNavOn}># brand-assets</span>
                  <span># contracts</span>
                  <span># marketing</span>
                </div>
                <div className={styles.previewMain}>
                  <div className={styles.previewRow}>
                    <b>logo-primary.svg</b>
                    <span>42 KB</span>
                  </div>
                  <div className={styles.previewRow}>
                    <b>brand-guidelines-v3.pdf</b>
                    <span>8.4 MB</span>
                  </div>
                  <div className={styles.previewRow}>
                    <b>campaign-hero.png</b>
                    <span>3.1 MB</span>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section className={styles.features} id="features" aria-labelledby="features-heading">
        <div className={styles.shell}>
          <div className={styles.sectionIntro}>
            <h2 id="features-heading">Built for how teams actually share files</h2>
            <p>
              Channeled keeps identity, membership, and file metadata in your database, and file
              bytes in storage you run — with permissions and audit from the start.
            </p>
          </div>

          <ul className={styles.featureGrid}>
            {features.map((feature) => (
              <li key={feature.id} className={styles.featureCard} data-testid={`landing-feature-${feature.id}`}>
                <span className={styles.featureIcon}>
                  <feature.Icon />
                </span>
                <h3>{feature.title}</h3>
                <p>{feature.body}</p>
              </li>
            ))}
          </ul>
        </div>
      </section>

      <section className={styles.how} aria-labelledby="how-heading">
        <div className={`${styles.shell} ${styles.split}`}>
          <div className={styles.splitCopy}>
            <h2 id="how-heading">A simple hierarchy</h2>
            <p>Teams, then channels, then files. Personal space sits beside teams, not inside them.</p>
          </div>
          <ol className={styles.flow}>
            {hierarchy.map((step, index) => (
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
        </div>
      </section>

      <section className={styles.cta} aria-labelledby="cta-heading">
        <div className={`${styles.shell} ${styles.split}`}>
          <div className={styles.splitCopy}>
            <h2 id="cta-heading">Start on your own server</h2>
            <p>Create an account, join a team, and open a channel. Your files stay where you put them.</p>
            <div className={styles.actions}>
              <Link to="/signup" className={styles.primary}>
                Create account
              </Link>
              <Link to="/login" className={styles.secondaryOnDark}>
                Log in
              </Link>
            </div>
          </div>
          <div className={styles.ctaVisual} aria-hidden="true">
            <article className={styles.ctaCard}>
              <span className={styles.flowIcon}>
                <HostingIcon />
              </span>
              <span className={styles.flowLabel}>Your server</span>
            </article>
            <article className={styles.ctaCard}>
              <span className={styles.flowIcon}>
                <PermissionsIcon />
              </span>
              <span className={styles.flowLabel}>Your control</span>
            </article>
          </div>
        </div>
      </section>

      <footer className={styles.footer}>
        <div className={styles.shell}>
          <p className={styles.brandMark}>Channeled</p>
          <p className={styles.footerNote}>Self-hosted file sharing for teams.</p>
        </div>
      </footer>
    </div>
  );
}
