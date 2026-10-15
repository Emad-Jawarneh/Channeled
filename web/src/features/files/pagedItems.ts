import type { FileItem, ItemList } from "../../api/channeled";
import type { SortColumn } from "./SortableTh";

/** Server sort token for a column + direction (`name_asc`, `updated_desc`, …). */
export function sortParam(sort: SortColumn, dir: "asc" | "desc"): string {
  return `${sort}_${dir}`;
}

/**
 * Flattens infinite-query pages in order. Keyset paging never repeats rows, but a refetch racing an insert can
 * briefly overlap two pages; the first occurrence wins so React keys stay unique.
 */
export function mergePages(pages: readonly ItemList[] | undefined): FileItem[] {
  if (!pages) return [];
  const seen = new Set<string>();
  const out: FileItem[] = [];
  for (const page of pages) {
    for (const item of page.items) {
      if (seen.has(item.id)) continue;
      seen.add(item.id);
      out.push(item);
    }
  }
  return out;
}

/** Bulk bar label: selection only covers loaded rows while more pages exist. */
export function selectionLabel(count: number, hasMore: boolean): string {
  return hasMore ? `${count} selected · loaded items only` : `${count} selected`;
}
