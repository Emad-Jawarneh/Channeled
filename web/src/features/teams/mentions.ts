import type { MessageMention } from "../../api/channeled";

/** An in-progress `@query` token ending at the caret. `start` is the index of the `@`. */
export type MentionQuery = { start: number; query: string };

/** Finds an `@token` immediately before the caret (the `@` must start the text or follow whitespace). */
export function findMentionQuery(text: string, caret: number): MentionQuery | null {
  const before = text.slice(0, caret);
  const match = /(^|\s)@([^\s@]*)$/.exec(before);
  if (!match) return null;
  const query = match[2] ?? "";
  return { start: before.length - query.length - 1, query };
}

/** Case-insensitive filter: prefix matches first, then substring matches. */
export function filterMembers<T extends { displayName: string }>(members: T[], query: string, limit = 6): T[] {
  const q = query.trim().toLowerCase();
  if (!q) return members.slice(0, limit);
  const starts: T[] = [];
  const contains: T[] = [];
  for (const m of members) {
    const name = m.displayName.toLowerCase();
    if (name.startsWith(q)) starts.push(m);
    else if (name.includes(q)) contains.push(m);
  }
  return [...starts, ...contains].slice(0, limit);
}

function escapeRegExp(value: string): string {
  return value.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
}

/** Matches `@Name` for any of the names, longest first, not glued to surrounding word characters. */
function mentionRegex(names: string[]): RegExp | null {
  const unique = [...new Set(names.filter((n) => n.length > 0))].sort((a, b) => b.length - a.length);
  if (unique.length === 0) return null;
  return new RegExp(`(?<![\\p{L}\\p{N}_])@(${unique.map(escapeRegExp).join("|")})(?![\\p{L}\\p{N}_])`, "gu");
}

export type BodySegment = { text: string; mention?: MessageMention };

/** Splits a message body into plain and highlighted-mention segments (text only; never HTML). */
export function segmentBody(body: string, mentions: MessageMention[]): BodySegment[] {
  const regex = mentionRegex(mentions.map((m) => m.displayName));
  if (!regex) return [{ text: body }];
  const byName = new Map(mentions.map((m) => [m.displayName, m]));
  const segments: BodySegment[] = [];
  let last = 0;
  for (const match of body.matchAll(regex)) {
    const index = match.index ?? 0;
    if (index > last) segments.push({ text: body.slice(last, index) });
    segments.push({ text: match[0], mention: byName.get(match[1] ?? "") });
    last = index + match[0].length;
  }
  if (last < body.length) segments.push({ text: body.slice(last) });
  return segments;
}

/** Tracked mentions whose `@DisplayName` is still present in the draft (user may have edited it away). */
export function activeMentionIds(draft: string, tracked: MessageMention[]): string[] {
  const ids: string[] = [];
  for (const m of tracked) {
    const regex = mentionRegex([m.displayName]);
    if (regex && regex.test(draft) && !ids.includes(m.userId)) ids.push(m.userId);
  }
  return ids;
}
