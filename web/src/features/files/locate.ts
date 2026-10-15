import type { ItemLocation } from "../../api/channeled";

/** The listing a page is showing; used to decide whether a located item belongs here or needs navigation. */
export type ListView =
  | { scope: "channel"; teamSlug: string; channelName: string; folderId: string | null; trash: boolean }
  | { scope: "personal"; folderId: string | null; trash: boolean };

/** Row to focus once the location's page is open: the item itself, or its trash entry when trashed. */
export function focusIdFor(location: ItemLocation): string {
  return location.state === "trashed" ? (location.trashEntryId ?? location.item.id) : location.item.id;
}

/** Route that shows `location` with its row highlighted (`?item=`). */
export function pathForLocation(location: ItemLocation): string {
  const params = new URLSearchParams();
  if (location.state === "live" && location.folderId) params.set("folder", location.folderId);
  params.set("item", focusIdFor(location));
  const trash = location.state === "trashed" ? "/trash" : "";
  const base =
    location.scope === "personal"
      ? `/me/files${trash}`
      : `/t/${encodeURIComponent(location.teamSlug ?? "")}/c/${encodeURIComponent(location.channelName ?? "")}${trash}`;
  return `${base}?${params.toString()}`;
}

/** True when the page already shows the listing `location` lives in (same scope, channel, folder, trash view). */
export function locationMatchesView(location: ItemLocation, view: ListView): boolean {
  if (location.scope !== view.scope) return false;
  if (view.scope === "channel") {
    if (location.teamSlug !== view.teamSlug) return false;
    if ((location.channelName ?? "").toLowerCase() !== view.channelName.toLowerCase()) return false;
  }
  if (location.state === "trashed") return view.trash;
  return !view.trash && (location.folderId ?? null) === (view.folderId ?? null);
}
