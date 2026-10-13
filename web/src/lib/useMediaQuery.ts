import { useEffect, useState } from "react";

/** Subscribe to a CSS media query. SSR/first paint uses `false` until mounted. */
export function useMediaQuery(query: string): boolean {
  const [matches, setMatches] = useState(() => {
    if (typeof window === "undefined" || !window.matchMedia) return false;
    return window.matchMedia(query).matches;
  });

  useEffect(() => {
    const mql = window.matchMedia(query);
    const onChange = () => setMatches(mql.matches);
    onChange();
    mql.addEventListener("change", onChange);
    return () => mql.removeEventListener("change", onChange);
  }, [query]);

  return matches;
}

/**
 * File browser “compact” layout: preview auto-closes and search collapses to an icon.
 * AppShell still stacks columns at 900px.
 */
export const TABLET_MQ = "(max-width: 1450px)";
