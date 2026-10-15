import { Link } from "react-router-dom";
import { BrandLogo } from "../../brand/BrandLogo";
import { ThemeToggle } from "../../theme/ThemeToggle";
import { Button } from "../../ui/Button";
import styles from "./LandingPage.module.css";

const features = [
  {
    id: "channels",
    title: "Teams and channels",
    body: "Organize work by team, then by channel. Open channels for the whole team; private channels for the people who need them.",
  },
  {
    id: "personal",
    title: "Personal space",
    body: "Keep drafts in My files — private to you — then copy into a team channel when you are ready to share.",
  },
  {
    id: "permissions",
    title: "Clear permissions",
    body: "Owners, admins, members, and viewers map to real actions. Hidden resources stay hidden, including from people who should not know they exist.",
  },
  {
    id: "files",
    title: "Uploads you can trust",
    body: "Resumable uploads, folders, trash with restore, search across channel and personal space, and quotas you can see before you hit the wall.",
  },
  {
    id: "audit",
    title: "Audit and admin",
    body: "Every important action is recorded. Platform admins manage users, teams, storage health, and backups from one place.",
  },
  {
    id: "hosting",
    title: "Self-hosted by design",
    body: "Your files stay on your infrastructure. PostgreSQL for metadata, S3-compatible object storage for bytes — no SaaS lock-in.",
  },
] as const;

const roles = [
  { name: "Owner", detail: "Deletes the team and transfers ownership." },
  { name: "Admin", detail: "Manages channels, members, and anyone’s files." },
  { name: "Member", detail: "Uploads and edits their own files." },
  { name: "Viewer", detail: "Browses and downloads. Never writes." },
] as const;

const hierarchy = [
  { id: "teams", label: "Teams", detail: "Independent workspaces with their own members and quota." },
  { id: "channels", label: "Channels", detail: "File or text rooms. Open to the team, or listed members only." },
  { id: "files", label: "Files", detail: "Folders, versions you can restore, and search by name." },
] as const;

function CheckIcon() {
  return (
    <svg
      className={styles.check}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth={1.7}
      strokeLinecap="round"
      strokeLinejoin="round"
      aria-hidden="true"
    >
      <path d="M5 12l5 5 9-10" />
    </svg>
  );
}

function FeatureMark({ index }: { index: number }) {
  return (
    <span className={styles.featureMark} aria-hidden="true">
      {String(index).padStart(2, "0")}
    </span>
  );
}

function ProductPreview() {
  return (
    <div className={styles.app} aria-hidden="true">
      <div className={styles.rail}>
        <span className={styles.railBrand}>
          <BrandLogo decorative />
        </span>
        <span className={styles.railMe}>Me</span>
        <span className={styles.railTeamOn}>AC</span>
        <span className={styles.railTeam}>DS</span>
        <span className={styles.railTeam}>CW</span>
        <span className={styles.railAdd}>+</span>
      </div>
      <div className={styles.side}>
        <div className={styles.sideHead}>
          <span className={styles.sideMark}>A</span>
          <div>
            <b>Acme Design</b>
            <p>12 members · 48.2 GB</p>
          </div>
        </div>
        <span className={styles.sideNav}>Overview</span>
        <span className={styles.sideLabel}>Channels</span>
        <span className={styles.sideNavOn}># brand-assets</span>
        <span className={styles.sideNav}># contracts</span>
        <span className={styles.sideNav}># marketing</span>
      </div>
      <div className={styles.main}>
        <div className={styles.mainTop}>
          <span className={styles.search}>Search files</span>
          <span className={styles.topBtn}>Upload</span>
        </div>
        <div className={styles.tableHead}>
          <span>Name</span>
          <span>Size</span>
        </div>
        <div className={styles.tableRow}>
          <span className={styles.fileBadge} data-kind="svg">
            SVG
          </span>
          <b>logo-primary.svg</b>
          <span>42 KB</span>
        </div>
        <div className={`${styles.tableRow} ${styles.tableRowOn}`}>
          <span className={styles.fileBadge} data-kind="pdf">
            PDF
          </span>
          <b>guidelines-v3.pdf</b>
          <span>8.4 MB</span>
        </div>
        <div className={styles.tableRow}>
          <span className={styles.fileBadge} data-kind="png">
            PNG
          </span>
          <b>campaign-hero.png</b>
          <span>3.1 MB</span>
        </div>
      </div>
    </div>
  );
}

export function LandingPage() {
  return (
    <div className={styles.page}>
      <header className={styles.top}>
        <div className={styles.shell}>
          <a href="#top" className={styles.brandMark} data-testid="landing-brand">
            <BrandLogo decorative />
            Channeled
          </a>
          <nav className={styles.topNav} aria-label="Account">
            <a href="#features" className={styles.navLink}>
              Product
            </a>
            <a href="#how" className={styles.navLink}>
              How it works
            </a>
            <ThemeToggle compact />
            <Link to="/login" className={`${styles.navLink} ${styles.navLogin}`} data-testid="landing-login-nav">
              Log in
            </Link>
            <Button asChild variant="primary">
              <Link to="/signup" data-testid="landing-signup-nav">
                Sign up
              </Link>
            </Button>
          </nav>
        </div>
      </header>

      <main id="top">
        <section className={styles.hero} aria-label="Introduction">
          <div className={`${styles.shell} ${styles.heroGrid}`}>
            <div className={styles.heroCopy}>
              <p className={styles.eyebrow}>Self-hosted file sharing</p>
              <h1 className={styles.title}>Share files with your teams, on your own server.</h1>
              <p className={styles.lede}>
                Organized by team and channel, with clear permissions on every file. Personal space
                sits beside teams, not inside them.
              </p>
              <div className={styles.actions}>
                <Button asChild variant="primary">
                  <Link to="/signup" data-testid="landing-signup">
                    Sign up
                  </Link>
                </Button>
                <Button asChild>
                  <Link to="/login" data-testid="landing-login">
                    Log in
                  </Link>
                </Button>
              </div>
              <ul className={styles.points}>
                <li>
                  <CheckIcon /> Independent teams and channels
                </li>
                <li>
                  <CheckIcon /> Private, self-hosted storage
                </li>
                <li>
                  <CheckIcon /> A full audit trail
                </li>
              </ul>
            </div>
            <ProductPreview />
          </div>
        </section>

        <section className={styles.features} id="features" aria-labelledby="features-heading">
          <div className={styles.shell}>
            <div className={styles.sectionHead}>
              <h2 id="features-heading">Built for how teams actually share files</h2>
              <p>
                Identity, membership, and metadata live in your database. File bytes live in storage
                you run — with permissions and audit from the start.
              </p>
            </div>
            <ul className={styles.featureGrid}>
              {features.map((feature, index) => (
                <li
                  key={feature.id}
                  className={styles.featureCard}
                  data-testid={`landing-feature-${feature.id}`}
                >
                  <FeatureMark index={index + 1} />
                  <h3>{feature.title}</h3>
                  <p>{feature.body}</p>
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section className={styles.how} id="how" aria-labelledby="how-heading">
          <div className={styles.shell}>
            <div className={styles.sectionHead}>
              <h2 id="how-heading">A simple hierarchy</h2>
              <p>Teams, then channels, then files. Personal space sits beside teams, not inside them.</p>
            </div>
            <ol className={styles.steps}>
              {hierarchy.map((step, index) => (
                <li key={step.id} className={styles.step}>
                  <span className={styles.stepIndex}>{index + 1}</span>
                  <h3>{step.label}</h3>
                  <p>{step.detail}</p>
                </li>
              ))}
            </ol>
          </div>
        </section>

        <section className={styles.roles} aria-labelledby="roles-heading">
          <div className={`${styles.shell} ${styles.rolesGrid}`}>
            <div>
              <h2 id="roles-heading">Roles that match the work</h2>
              <p>
                One authorizer decides every action. The UI can hide a control; the server still
                decides. Resources you cannot see return as if they do not exist.
              </p>
            </div>
            <ul className={styles.roleList}>
              {roles.map((role) => (
                <li key={role.name}>
                  <b>{role.name}</b>
                  <span>{role.detail}</span>
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section className={styles.cta} aria-labelledby="cta-heading">
          <div className={styles.shell}>
            <h2 id="cta-heading">Start on your own server</h2>
            <p>Create an account, join a team, and open a channel. Your files stay where you put them.</p>
            <div className={styles.actions}>
              <Button asChild className={styles.ctaStrong}>
                <Link to="/signup">Create account</Link>
              </Button>
              <Button asChild className={styles.ctaGhost}>
                <Link to="/login">Log in</Link>
              </Button>
            </div>
          </div>
        </section>
      </main>

      <footer className={styles.footer}>
        <div className={styles.shell}>
          <p className={styles.brandMark}>
            <BrandLogo decorative />
            Channeled
          </p>
          <p className={styles.footerNote}>Self-hosted file sharing for teams.</p>
        </div>
      </footer>
    </div>
  );
}
