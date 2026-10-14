import { NavLink, Outlet } from "react-router-dom";
import { AppShell } from "../../components/AppShell";
import shell from "../../components/AppShell.module.css";
import styles from "./Account.module.css";

function AccountNav() {
  return (
    <div>
      <p className={shell.sidebarTitle}>Account</p>
      <nav className={shell.sideNav} aria-label="Account">
        <NavLink to="/account/profile">Profile</NavLink>
        <NavLink to="/account/security">Security</NavLink>
        <NavLink to="/account/sessions">Sessions</NavLink>
      </nav>
    </div>
  );
}

export function AccountLayout() {
  return (
    <AppShell title="Account" sidebar={<AccountNav />}>
      <div className={styles.panel}>
        <Outlet />
      </div>
    </AppShell>
  );
}
