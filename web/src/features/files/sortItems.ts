import { ItemKind, type FileItem } from "../../api/channeled";
import { formatFileType } from "../../lib/describeFileKind";
import type { SortColumn } from "./SortableTh";

/** Sort list rows. Name sort always groups folders before files. */
export function compareFileItems(
  a: FileItem,
  b: FileItem,
  sort: SortColumn,
  sortDir: "asc" | "desc",
): number {
  const dir = sortDir === "asc" ? 1 : -1;

  if (sort === "name") {
    const ak = a.kind === ItemKind.Folder ? 0 : 1;
    const bk = b.kind === ItemKind.Folder ? 0 : 1;
    if (ak !== bk) return ak - bk;
    return a.name.localeCompare(b.name) * dir;
  }

  if (sort === "size") {
    return ((a.sizeBytes ?? 0) - (b.sizeBytes ?? 0)) * dir;
  }
  if (sort === "updated") {
    return (new Date(a.updatedAt).getTime() - new Date(b.updatedAt).getTime()) * dir;
  }
  if (sort === "type") {
    return formatFileType(a).localeCompare(formatFileType(b)) * dir;
  }
  return a.name.localeCompare(b.name) * dir;
}
