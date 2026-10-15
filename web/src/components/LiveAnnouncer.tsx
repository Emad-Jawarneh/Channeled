import { useCallback, useRef, useState, type ReactNode } from "react";
import { AnnounceContext, type Announce } from "./liveAnnouncerContext";

/**
 * One polite live region mounted above the routes. Regions created together with a page are often not
 * read by screen readers, so pages announce through this provider instead of owning their own region.
 */
export function LiveAnnouncerProvider({ children }: { children: ReactNode }) {
  const [message, setMessage] = useState("");
  const timer = useRef<number | null>(null);

  const announce = useCallback<Announce>((next) => {
    // Clear first so repeating the same text is announced again.
    setMessage("");
    if (timer.current !== null) window.clearTimeout(timer.current);
    timer.current = window.setTimeout(() => setMessage(next), 50);
  }, []);

  return (
    <AnnounceContext.Provider value={announce}>
      {children}
      <div className="visually-hidden" role="status" aria-live="polite" aria-atomic="true" data-testid="live-announcer">
        {message}
      </div>
    </AnnounceContext.Provider>
  );
}
