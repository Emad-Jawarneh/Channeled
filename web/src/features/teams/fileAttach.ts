/** An in-progress `#query` token ending at the caret. `start` is the index of `#`. */
export type FileAttachQuery = { start: number; query: string };

/** Finds a `#token` immediately before the caret (must start the text or follow whitespace). */
export function findFileAttachQuery(text: string, caret: number): FileAttachQuery | null {
  const before = text.slice(0, caret);
  const match = /(^|\s)#([^\s#]*)$/.exec(before);
  if (!match) return null;
  const query = match[2] ?? "";
  return { start: before.length - query.length - 1, query };
}
