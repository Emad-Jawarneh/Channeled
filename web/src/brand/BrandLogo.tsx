import logo from "../media/Logo.png";
import styles from "./BrandLogo.module.css";

type Props = {
  className?: string;
  /** Set when the word “Channeled” is already visible next to the mark. */
  decorative?: boolean;
};

export function BrandLogo({ className, decorative = false }: Props) {
  return (
    <img
      src={logo}
      alt={decorative ? "" : "Channeled"}
      className={[styles.logo, className ?? ""].filter(Boolean).join(" ")}
      width={40}
      height={40}
      decoding="async"
    />
  );
}
