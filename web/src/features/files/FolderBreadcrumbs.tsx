import { useMemo, type DragEvent } from "react";
import type { FolderPathSegment } from "../../api/channeled";
import { collapsePath } from "./collapsePath";
import styles from "./FolderBreadcrumbs.module.css";

type Props = {
  rootLabel: string;
  /** Current leaf from the URL (`?folder=`). Null/undefined = root. */
  folderId?: string | null;
  /** Name hint while list.path is loading / unavailable. */
  folderNameHint?: string | null;
  path?: FolderPathSegment[] | null;
  onNavigate: (folderId: string | null, name?: string) => void;
  /** When set, crumbs (except current) accept internal item drops. */
  onDropItems?: (targetFolderId: string | null) => void;
  dropActiveId?: string | null | "root";
  onDropTargetChange?: (id: string | null | "root") => void;
};

export function FolderBreadcrumbs({
  rootLabel,
  folderId = null,
  folderNameHint = null,
  path,
  onNavigate,
  onDropItems,
  dropActiveId,
  onDropTargetChange,
}: Props) {
  const segments = useMemo(() => {
    const root = { id: null as string | null, name: rootLabel };
    const fromApi = path ?? [];
    // Inside a folder but API omitted path — still show a trail so root is clickable.
    if (folderId && fromApi.length === 0) {
      return [root, { id: folderId, name: folderNameHint?.trim() || "Folder" }];
    }
    return [root, ...fromApi];
  }, [rootLabel, path, folderId, folderNameHint]);

  const visible = useMemo(() => collapsePath(segments), [segments]);
  const currentId = folderId ?? null;

  // At channel/personal root the trail is just the root label — same as the page heading.
  if (!folderId) {
    return null;
  }

  function bindDrop(targetId: string | null) {
    if (!onDropItems) return {};
    const key = targetId ?? "root";
    return {
      onDragEnter: (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        onDropTargetChange?.(key);
      },
      onDragOver: (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        if (e.dataTransfer) e.dataTransfer.dropEffect = "move";
      },
      onDragLeave: (e: DragEvent) => {
        e.stopPropagation();
        const related = e.relatedTarget as Node | null;
        if (related && e.currentTarget.contains(related)) return;
        onDropTargetChange?.(null);
      },
      onDrop: (e: DragEvent) => {
        e.preventDefault();
        e.stopPropagation();
        onDropTargetChange?.(null);
        onDropItems(targetId);
      },
      "data-drop-active": dropActiveId === key ? "true" : undefined,
    };
  }

  return (
    <nav className={styles.nav} aria-label="Folder path" data-testid="folder-breadcrumbs">
      <ol className={styles.list}>
        {visible.map((seg, index) => {
          const isEllipsis = seg.id === "ellipsis";
          const isCurrent = !isEllipsis && (seg.id ?? null) === currentId;
          return (
            <li key={isEllipsis ? `ellipsis-${index}` : (seg.id ?? "root")} className={styles.item}>
              {index > 0 && (
                <span className={styles.sep} aria-hidden="true">
                  ›
                </span>
              )}
              {isEllipsis ? (
                <span className={styles.ellipsis} aria-hidden="true">
                  …
                </span>
              ) : isCurrent ? (
                <span className={styles.current} aria-current="page" title={seg.name}>
                  {seg.name}
                </span>
              ) : (
                <button
                  type="button"
                  className={styles.crumb}
                  title={seg.name}
                  data-testid={seg.id ? `crumb-${seg.name}` : "crumb-root"}
                  onClick={() => onNavigate(seg.id, seg.name)}
                  {...bindDrop(seg.id)}
                >
                  {seg.name}
                </button>
              )}
            </li>
          );
        })}
      </ol>
    </nav>
  );
}
