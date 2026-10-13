export type PathSegment = { id: string | null; name: string };

/**
 * Collapse middle segments for narrow crumb trails.
 * Always keeps root, optional ellipsis, then last `keepTail` segments.
 */
export function collapsePath<T extends PathSegment>(
  segments: T[],
  maxVisible = 4,
  keepTail = 2,
): Array<T | { id: "ellipsis"; name: "…" }> {
  if (segments.length <= maxVisible) return segments;
  const tail = Math.min(keepTail, Math.max(0, segments.length - 1));
  const head = segments[0]!;
  const rest = segments.slice(-tail);
  return [head, { id: "ellipsis", name: "…" }, ...rest];
}
