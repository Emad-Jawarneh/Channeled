import { memo, type DragEvent, type MouseEvent as ReactMouseEvent } from "react";
import { ItemKind, type FileItem } from "../../api/channeled";
import { UserChip } from "../../components/UserChip";
import { formatFileType } from "../../lib/describeFileKind";
import { formatBytes } from "../../lib/formatBytes";
import { ModifiedAt } from "../../lib/formatModified";
import { FileThumb } from "./FileThumb";
import styles from "./FileBrowser.module.css";

/** Stable callbacks shared by every row (see useStableCallback) so memoized rows skip unrelated renders. */
export type FileRowHandlers = {
  onSelect: (item: FileItem) => void;
  onToggleCheck: (id: string) => void;
  onOpenFolder: (item: FileItem) => void;
  onOpenMenu: (id: string, trigger: HTMLElement) => void;
  onContextMenu: (id: string, e: ReactMouseEvent) => void;
  onDragStart: (e: DragEvent, item: FileItem) => void;
  onDragEnd: () => void;
  folderDrop: (id: string) => Record<string, (e: DragEvent) => void>;
};

export type FileRowProps = {
  item: FileItem;
  rowTestIdPrefix: string;
  openFolderTestIdPrefix: string;
  contentUrl: string | null;
  selected: boolean;
  checked: boolean;
  cut: boolean;
  dropActive: boolean;
  menuOpen: boolean;
  pinned: boolean;
  showCheckbox: boolean;
  draggable: boolean;
  contextMenu: boolean;
  /** Folder rows: glyph + open button (navigable) vs. a plain "name/" label. */
  folderNavigable: boolean;
  acceptsDrop: boolean;
  handlers: FileRowHandlers;
};

/**
 * One file-table row. Memoized: selecting a row, ticking a checkbox or opening a menu must only re-render the
 * rows whose props changed (perf-probe measured ~420–650 ms interactions at 1,000–2,000 rows before this).
 */
export const FileRow = memo(function FileRow({
  item,
  rowTestIdPrefix,
  openFolderTestIdPrefix,
  contentUrl,
  selected,
  checked,
  cut,
  dropActive,
  menuOpen,
  pinned,
  showCheckbox,
  draggable,
  contextMenu,
  folderNavigable,
  acceptsDrop,
  handlers,
}: FileRowProps) {
  const isFolder = item.kind === ItemKind.Folder;
  return (
    <tr
      data-testid={`${rowTestIdPrefix}-${item.name}`}
      data-item-id={item.id}
      data-pinned={pinned ? "true" : undefined}
      tabIndex={-1}
      data-selected={selected || checked ? "true" : "false"}
      data-cut={cut ? "true" : undefined}
      data-drop-active={isFolder && dropActive ? "true" : undefined}
      draggable={draggable}
      onDragStart={(e) => handlers.onDragStart(e, item)}
      onDragEnd={handlers.onDragEnd}
      onClick={() => handlers.onSelect(item)}
      onContextMenu={(e) => {
        if (!contextMenu) return;
        handlers.onContextMenu(item.id, e);
      }}
      {...(isFolder && acceptsDrop ? handlers.folderDrop(item.id) : {})}
    >
      <td onClick={(e) => e.stopPropagation()}>
        {showCheckbox && (
          <input
            type="checkbox"
            className={styles.check}
            data-testid={`check-${item.name}`}
            checked={checked}
            aria-label={`Select ${item.name}`}
            onChange={() => handlers.onToggleCheck(item.id)}
            onMouseDown={(e) => e.stopPropagation()}
          />
        )}
      </td>
      <td>
        <div className={styles.nameCell}>
          {isFolder && folderNavigable ? (
            <span className={styles.folderGlyph} aria-hidden="true">
              ▦
            </span>
          ) : (
            <FileThumb item={item} contentUrl={contentUrl} />
          )}
          <div className={styles.nameText}>
            {isFolder && folderNavigable ? (
              <button
                type="button"
                className={styles.linkish}
                data-testid={`${openFolderTestIdPrefix}-${item.name}`}
                onClick={(e) => {
                  e.stopPropagation();
                  handlers.onOpenFolder(item);
                }}
                onDoubleClick={(e) => {
                  e.stopPropagation();
                  handlers.onOpenFolder(item);
                }}
                onMouseDown={(e) => e.stopPropagation()}
              >
                {item.name}
              </button>
            ) : (
              <span className={styles.linkish}>{isFolder ? `${item.name}/` : item.name}</span>
            )}
            {pinned && (
              <span className={styles.pinnedPill} data-testid="pinned-pill">
                Found
              </span>
            )}
          </div>
        </div>
      </td>
      <td>
        <span className={styles.typeCell}>{formatFileType(item)}</span>
      </td>
      <td>{isFolder ? "—" : formatBytes(item.sizeBytes)}</td>
      <td className={styles.uploaderCell} onClick={(e) => e.stopPropagation()}>
        {!isFolder && item.uploadedBy && item.uploadedByDisplayName ? (
          <UserChip
            userId={item.uploadedBy}
            displayName={item.uploadedByDisplayName}
            username={item.uploadedByUsername ?? undefined}
            avatarVersion={item.uploadedByAvatarVersion}
          />
        ) : (
          "—"
        )}
      </td>
      <td>
        <ModifiedAt value={item.updatedAt} />
      </td>
      <td>
        <div className={styles.actionWrap}>
          <button
            type="button"
            className={styles.moreBtn}
            aria-label={`Actions for ${item.name}`}
            aria-haspopup="menu"
            aria-expanded={menuOpen}
            data-open={menuOpen ? "true" : undefined}
            data-menu-trigger={item.id}
            data-testid={`actions-${item.name}`}
            onClick={(e) => {
              e.stopPropagation();
              handlers.onOpenMenu(item.id, e.currentTarget);
            }}
            onMouseDown={(e) => e.stopPropagation()}
          >
            <span className={styles.moreDots} aria-hidden="true">
              <span />
              <span />
              <span />
            </span>
          </button>
        </div>
      </td>
    </tr>
  );
});
