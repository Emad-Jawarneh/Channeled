import { createContext, useContext } from "react";

export type Announce = (message: string) => void;

export const AnnounceContext = createContext<Announce>(() => {});

/** Announces `message` through the root polite live region (LiveAnnouncerProvider). */
export function useAnnounce(): Announce {
  return useContext(AnnounceContext);
}
