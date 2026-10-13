import { teamLogoUrl, type Team } from "../../api/channeled";
import styles from "./TeamMark.module.css";

type Props = {
  team: Pick<Team, "name" | "slug" | "hasLogo" | "logoUpdatedAt">;
  size?: "sm" | "md" | "lg";
  className?: string;
};

export function TeamMark({ team, size = "md", className }: Props) {
  const logo = teamLogoUrl(team);
  const initials = team.name.trim().slice(0, 2).toUpperCase() || "?";
  const classes = [styles.mark, styles[size], className].filter(Boolean).join(" ");

  if (logo) {
    return (
      <span className={classes} title={team.name}>
        <img className={styles.image} src={logo} alt="" />
      </span>
    );
  }

  return (
    <span className={classes} title={team.name} aria-hidden="true">
      {initials}
    </span>
  );
}
