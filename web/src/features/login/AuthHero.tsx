import { BrandLogo } from "../../brand/BrandLogo";
import { ThemeToggle } from "../../theme/ThemeToggle";
import styles from "./LoginPage.module.css";

const POINTS = [
  "Independent teams and channels",
  "Private, self-hosted storage",
  "A full audit trail",
] as const;

function CheckIcon() {
  return (
    <svg
      className={styles.checkIcon}
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

type AuthHeroProps = {
  title: string;
  lede: string;
};

export function AuthHero({ title, lede }: AuthHeroProps) {
  return (
    <section className={styles.hero} aria-label="About Channeled">
      <div className={styles.heroTop}>
        <p className={styles.brandRow}>
          <BrandLogo decorative />
          Channeled
        </p>
        <ThemeToggle compact />
      </div>

      <div className={styles.heroCopy}>
        <h1 className={styles.heroTitle}>{title}</h1>
        <p className={styles.heroLede}>{lede}</p>
      </div>

      <div className={styles.heroPreview} aria-hidden="true">
        <div className={styles.previewPlate} />
        <div className={styles.previewFileCard}>
          <b># brand-assets</b>
          <div className={styles.previewRows}>
            <div className={styles.previewRow}>
              <span className={styles.fileBadge} data-kind="pdf">
                PDF
              </span>
              <b>guidelines-v3.pdf</b>
            </div>
            <div className={styles.previewRow}>
              <span className={styles.fileBadge} data-kind="png">
                PNG
              </span>
              <b>campaign-hero.png</b>
            </div>
            <div className={styles.previewRow}>
              <span className={styles.fileBadge} data-kind="svg">
                SVG
              </span>
              <b>logo-primary.svg</b>
            </div>
          </div>
        </div>
        <div className={styles.previewActivity}>
          <span className={styles.previewAvatar}>OS</span>
          <span>
            <b>Omar S.</b> uploaded 2 files
          </span>
        </div>
      </div>

      <div className={styles.heroPoints}>
        {POINTS.map((label) => (
          <p key={label}>
            <CheckIcon />
            {label}
          </p>
        ))}
      </div>
    </section>
  );
}
