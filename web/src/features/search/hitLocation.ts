import type { SearchHit, Team } from "../../api/channeled";

/** Prefer live team name (API field or teams list) over the stable URL slug. */
export function teamLabelForHit(hit: SearchHit, teams?: readonly Team[] | null): string | null {
  if (hit.teamName?.trim()) return hit.teamName.trim();
  if (hit.teamSlug && teams?.length) {
    const match = teams.find((t) => t.slug === hit.teamSlug);
    if (match?.name) return match.name;
  }
  return hit.teamSlug ?? null;
}

export function hitLocationLabel(hit: SearchHit, teams?: readonly Team[] | null): string {
  const team = teamLabelForHit(hit, teams);
  if (hit.channelName && team) return `${team} · #${hit.channelName}`;
  return "My files";
}

export function hitLocationLink(hit: SearchHit): string {
  if (hit.teamSlug && hit.channelName) {
    return `/t/${hit.teamSlug}/c/${hit.channelName}`;
  }
  return "/me/files";
}
