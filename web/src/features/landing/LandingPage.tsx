import { Link } from "react-router-dom";
import styles from "./LandingPage.module.css";

export function LandingPage() {
  return (
    <div className={styles.page}>
      <div className={styles.visual} aria-hidden="true">
        <img
          className={styles.heroImage}
          src="https://images.unsplash.com/photo-1497366216548-37526070297c?auto=format&fit=crop&w=2400&q=80"
          alt=""
        />
        <div className={styles.veil} />
      </div>

      <header className={styles.top}>
        <p className={styles.brandMark} data-testid="landing-brand">
          Channeled
        </p>
        <nav className={styles.topNav} aria-label="Account">
          <Link to="/login" className={styles.navLink} data-testid="landing-login-nav">
            Log in
          </Link>
          <Link to="/signup" className={styles.navCta} data-testid="landing-signup-nav">
            Sign up
          </Link>
        </nav>
      </header>

      <main className={styles.hero}>
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
      </main>
    </div>
  );
}
