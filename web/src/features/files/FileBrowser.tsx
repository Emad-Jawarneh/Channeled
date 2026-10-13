import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState, type DragEvent } from "react";
import { createPortal } from "react-dom";
import {
  ChannelKind,
  filesApi,
  ItemKind,
  MemberRole,
  teamsApi,
  type FileItem,
  type FolderPathSegment,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { useAuth } from "../../auth/AuthContext";
import { AppShell } from "../../components/AppShell";
import { formatFileType } from "../../lib/describeFileKind";
import { formatBytes } from "../../lib/formatBytes";
import { ModifiedAt } from "../../lib/formatModified";
import { triggerDownload, triggerDownloads } from "../../lib/triggerDownload";
import { TABLET_MQ, useMediaQuery } from "../../lib/useMediaQuery";
import { useChannelFilesChanged } from "../../realtime/useChannelHub";
import { DropOverlay } from "../../uploads/DropOverlay";
import { useFileDrop } from "../../uploads/useFileDrop";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { ChannelSidebar } from "../teams/ChannelSidebar";
import { BulkBar } from "./BulkBar";
import { canMoveItem } from "./canMoveItem";
import { CreateFolderDialog } from "./CreateFolderDialog";
import {
  CHANNELED_ITEMS_MIME,
  clearDraggedItemIds,
  getDraggedItemIds,
  setDraggedItemIds,
} from "./dragItems";
import { FileThumb } from "./FileThumb";
import { FilePreviewPanel } from "./FilePreviewPanel";
import { FilesSearchField } from "./FilesSearchField";
import { FolderBreadcrumbs } from "./FolderBreadcrumbs";
import {
  MoveProgress,
  MoveSummary,
  UndoToast,
  type UndoToastState,
} from "./MoveFeedback";
import { moveBatch, summarizeMoveResult, type MoveBatchPrior } from "./moveBatch";
import { PickListDialog } from "./PickListDialog";
import { SelectionControls } from "./SelectionControls";
import { ShareLinkDialog } from "./ShareLinkDialog";
import { nextSortState, SortableTh, type SortColumn } from "./SortableTh";
import { UploadButton } from "./UploadButton";
import { useFolderNav } from "./useFolderNav";
import styles from "./FileBrowser.module.css";

type MenuState = { id: string; top: number; left: number };

type Props = {
  teamSlug: string;
  channelName: string;
  trash?: boolean;
};

export function FileBrowser({ teamSlug, channelName, trash = false }: Props) {
  const qc = useQueryClient();
  const uploads = useUploads();
  const { user } = useAuth();
  const { folderId, folderNameHint, openFolder, goToSegment, clearInvalidFolder, goToRoot } =
    useFolderNav(!trash);

  useEffect(() => {
    if (trash) {
      goToRoot({ replace: true });
    }
  }, [trash, goToRoot]);

  const team = useQuery({
    queryKey: ["team", teamSlug],
    queryFn: () => teamsApi.get(teamSlug),
  });
  const myRole = team.data?.myRole ?? MemberRole.Viewer;

  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });
  const channelId = (channels.data ?? []).find((c) => c.name === channelName)?.id;
  useChannelFilesChanged(channelId, teamSlug, channelName);

  const [selected, setSelected] = useState<FileItem | null>(null);
  const isTablet = useMediaQuery(TABLET_MQ);
  const [previewOpen, setPreviewOpen] = useState(() => {
    try {
      return localStorage.getItem("channeled.previewOpen") !== "0";
    } catch {
      return true;
    }
  });
  // Keep the preference, but never show the panel on tablet widths.
  const showPreview = previewOpen && !isTablet;

  useEffect(() => {
    try {
      localStorage.setItem("channeled.previewOpen", previewOpen ? "1" : "0");
    } catch {
      /* ignore */
    }
  }, [previewOpen]);
  const [sort, setSort] = useState<SortColumn>("name");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [nameQuery, setNameQuery] = useState("");
  const [menu, setMenu] = useState<MenuState | null>(null);
  const menuRef = useRef<HTMLUListElement | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [folderUnavailable, setFolderUnavailable] = useState(false);
  const [createFolderOpen, setCreateFolderOpen] = useState(false);
  const [createFolderError, setCreateFolderError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [shareItem, setShareItem] = useState<FileItem | null>(null);
  const [checked, setChecked] = useState<Set<string>>(() => new Set());
  const [moveOpen, setMoveOpen] = useState(false);
  const [singleMoveItem, setSingleMoveItem] = useState<FileItem | null>(null);
  const [sendOpen, setSendOpen] = useState(false);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkError, setBulkError] = useState<string | null>(null);
  const [moveProgress, setMoveProgress] = useState<{ done: number; total: number } | null>(null);
  const [moveSummary, setMoveSummary] = useState<{
    message: string;
    failures: { id: string; name?: string; message: string }[];
  } | null>(null);
  const [undoToast, setUndoToast] = useState<UndoToastState | null>(null);
  const [dropTargetId, setDropTargetId] = useState<string | null | "root">(null);
  const dropDepth = useRef(0);

  useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(null);
    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (menuRef.current?.contains(target)) return;
      if ((target as Element).closest?.(`[data-menu-trigger="${menu.id}"]`)) return;
      close();
    };
    window.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("scroll", close, true);
    window.addEventListener("resize", close);
    return () => {
      window.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("scroll", close, true);
      window.removeEventListener("resize", close);
    };
  }, [menu]);

  function openMenu(itemId: string, trigger: HTMLElement) {
    if (menu?.id === itemId) {
      setMenu(null);
      return;
    }
    const rect = trigger.getBoundingClientRect();
    const width = 160;
    const left = Math.min(Math.max(8, rect.right - width), window.innerWidth - width - 8);
    setMenu({ id: itemId, top: rect.bottom + 4, left });
  }

  function closeMenu() {
    setMenu(null);
  }

  const listKey = ["items", teamSlug, channelName, folderId, trash] as const;
  const list = useQuery({
    queryKey: listKey,
    queryFn: () => filesApi.list(teamSlug, channelName, { folderId, trash }),
    retry: (count, err) => {
      if (err instanceof ApiError && err.status === 404) return false;
      return count < 2;
    },
  });

  useEffect(() => {
    if (!folderId) return;
    if (list.error instanceof ApiError && list.error.status === 404) {
      clearInvalidFolder();
      setFolderUnavailable(true);
    }
  }, [list.error, folderId, clearInvalidFolder]);

  useEffect(() => {
    if (list.isSuccess && folderId) {
      setFolderUnavailable(false);
    }
  }, [list.isSuccess, folderId]);

  useEffect(() => {
    setChecked(new Set());
    setSelected(null);
    setMoveSummary(null);
  }, [folderId, trash, teamSlug, channelName]);

  const seenDone = useRef(new Set<string>());
  useEffect(() => {
    let refreshed = false;
    for (const job of uploads.jobs) {
      if (job.status !== "done" || seenDone.current.has(job.id)) {
        continue;
      }
      if (job.scope === "channel" && job.teamSlug === teamSlug && job.channelName === channelName) {
        seenDone.current.add(job.id);
        refreshed = true;
      }
    }
    if (refreshed) {
      void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
    }
  }, [uploads.jobs, teamSlug, channelName, qc]);

  const rename = useMutation({
    mutationFn: ({ id, name }: { id: string; name: string }) =>
      filesApi.rename(teamSlug, channelName, id, name),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] }),
  });

  const trashMut = useMutation({
    mutationFn: (id: string) => filesApi.trash(teamSlug, channelName, id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] }),
  });

  const restore = useMutation({
    mutationFn: (id: string) => filesApi.restore(teamSlug, channelName, id),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] }),
  });

  const folders = useMemo(
    () => (list.data?.items ?? []).filter((i) => i.kind === ItemKind.Folder && !i.deletedAt),
    [list.data],
  );

  const path =
    list.data?.path ?? (list.data as { Path?: FolderPathSegment[] } | undefined)?.Path ?? null;
  const currentFolderName = path?.at(-1)?.name ?? folderNameHint ?? undefined;

  const visibleItems = useMemo(() => {
    let items = list.data?.items ?? [];
    const q = nameQuery.trim().toLowerCase();
    if (q) {
      items = items.filter((i) => i.name.toLowerCase().includes(q));
    }
    const dir = sortDir === "asc" ? 1 : -1;
    return [...items].sort((a, b) => {
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
    });
  }, [list.data, nameQuery, sort, sortDir]);

  function onSortColumn(column: SortColumn) {
    const next = nextSortState(sort, sortDir, column);
    setSort(next.sort);
    setSortDir(next.sortDir);
  }

  const itemMovable = useCallback(
    (item: FileItem) =>
      canMoveItem({ item, myUserId: user?.id, myRole }),
    [user?.id, myRole],
  );

  const checkedItems = useMemo(
    () => visibleItems.filter((i) => checked.has(i.id)),
    [visibleItems, checked],
  );
  const movableChecked = useMemo(
    () => checkedItems.filter(itemMovable),
    [checkedItems, itemMovable],
  );
  const allVisibleChecked =
    visibleItems.length > 0 && visibleItems.every((i) => checked.has(i.id));
  const someVisibleChecked = checkedItems.length > 0 && !allVisibleChecked;

  const movePickerOptions = useMemo(() => {
    const exclude = new Set(checked);
    if (singleMoveItem) exclude.add(singleMoveItem.id);
    const ancestors = (path ?? [])
      .filter((p) => p.id !== folderId)
      .map((p) => ({ id: p.id, label: p.name }));
    const siblings = folders
      .filter((f) => !exclude.has(f.id))
      .map((f) => ({ id: f.id, label: f.name }));
    const seen = new Set<string>();
    const options: { id: string; label: string }[] = [];
    for (const opt of [...ancestors, ...siblings]) {
      if (seen.has(opt.id)) continue;
      seen.add(opt.id);
      options.push(opt);
    }
    return options;
  }, [path, folderId, folders, checked, singleMoveItem]);

  const sendChannelOptions = useMemo(
    () =>
      (channels.data ?? [])
        .filter(
          (c) =>
            (c.kind ?? ChannelKind.File) === ChannelKind.File && c.name !== channelName,
        )
        .map((c) => ({ id: c.name, label: `#${c.name}` })),
    [channels.data, channelName],
  );

  function toggleChecked(id: string) {
    setChecked((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAllVisible() {
    setChecked((prev) => {
      if (allVisibleChecked) {
        const next = new Set(prev);
        for (const item of visibleItems) next.delete(item.id);
        return next;
      }
      const next = new Set(prev);
      for (const item of visibleItems) next.add(item.id);
      return next;
    });
  }

  function selectAllVisible() {
    setChecked((prev) => {
      const next = new Set(prev);
      for (const item of visibleItems) next.add(item.id);
      return next;
    });
  }

  function selectNone() {
    setChecked(new Set());
  }

  function invertVisible() {
    setChecked((prev) => {
      const next = new Set(prev);
      for (const item of visibleItems) {
        if (next.has(item.id)) next.delete(item.id);
        else next.add(item.id);
      }
      return next;
    });
  }

  const invalidateListings = useCallback(
    (targetFolderId: string | null, targetChannelName?: string | null) => {
      void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
      if (targetChannelName && targetChannelName !== channelName) {
        void qc.invalidateQueries({ queryKey: ["items", teamSlug, targetChannelName] });
      } else if (targetFolderId !== (folderId ?? null)) {
        void qc.invalidateQueries({
          queryKey: ["items", teamSlug, channelName, targetFolderId ?? undefined],
        });
      }
    },
    [qc, teamSlug, channelName, folderId],
  );

  const runMove = useCallback(
    async (
      items: MoveBatchPrior[],
      targetFolderId: string | null,
      targetChannelName?: string | null,
      opts?: { undoable?: boolean },
    ) => {
      if (items.length === 0) return;
      setBulkBusy(true);
      setBulkError(null);
      setMoveProgress({ done: 0, total: items.length });
      setMoveSummary(null);
      try {
        const result = await moveBatch({
          items,
          targetFolderId,
          targetChannelName,
          moveOne: (id, folder, channel) =>
            filesApi.move(teamSlug, channelName, id, folder, channel),
          onProgress: (done, total) => setMoveProgress({ done, total }),
        });
        setMoveSummary({
          message: summarizeMoveResult(result),
          failures: result.failures,
        });
        setChecked((prev) => {
          const next = new Set(prev);
          for (const s of result.successes) next.delete(s.id);
          return next;
        });
        invalidateListings(targetFolderId, targetChannelName);
        if (opts?.undoable !== false && result.successes.length > 0 && !targetChannelName) {
          const undos = result.successes;
          setUndoToast({
            message: `Moved ${undos.length} item${undos.length === 1 ? "" : "s"}`,
            onUndo: () => {
              setUndoToast(null);
              void (async () => {
                setBulkBusy(true);
                setMoveProgress({ done: 0, total: undos.length });
                try {
                  const undoResult = await moveBatch({
                    items: undos.map((u) => ({
                      id: u.id,
                      previousFolderId: targetFolderId,
                      name: u.name,
                    })),
                    targetFolderId: null,
                    moveOne: async (id) => {
                      const prior = undos.find((u) => u.id === id)!;
                      await filesApi.move(teamSlug, channelName, id, prior.previousFolderId);
                    },
                    onProgress: (done, total) => setMoveProgress({ done, total }),
                  });
                  setMoveSummary({
                    message: summarizeMoveResult({
                      successes: undoResult.successes,
                      failures: undoResult.failures,
                    }).replace(/^Moved/, "Restored"),
                    failures: undoResult.failures,
                  });
                  void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
                } finally {
                  setBulkBusy(false);
                  setMoveProgress(null);
                }
              })();
            },
          });
        }
      } finally {
        setBulkBusy(false);
        setMoveProgress(null);
      }
    },
    [teamSlug, channelName, invalidateListings, qc],
  );

  function contentUrlFor(item: FileItem, opts?: { download?: boolean }) {
    if (item.kind === ItemKind.Folder) return null;
    return filesApi.contentUrl(teamSlug, channelName, item.id, opts);
  }

  async function downloadItem(item: FileItem) {
    const url = contentUrlFor(item, { download: true });
    if (!url) return;
    try {
      setError(null);
      await triggerDownload(url, item.name);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Download failed.");
    }
  }

  async function downloadChecked() {
    const items = visibleItems
      .filter((i) => checked.has(i.id) && i.kind === ItemKind.File)
      .map((i) => ({
        url: filesApi.contentUrl(teamSlug, channelName, i.id, { download: true }),
        fileName: i.name,
      }));
    try {
      setError(null);
      await triggerDownloads(items);
    } catch (e) {
      setError(e instanceof Error ? e.message : "Download failed.");
    }
  }

  const details = (
    <FilePreviewPanel
      item={selected}
      contentUrl={selected ? contentUrlFor(selected) : null}
      showDownload={!trash}
      onDownload={selected ? () => downloadItem(selected) : undefined}
    />
  );

  async function onRename(item: FileItem) {
    const name = window.prompt("Rename to", item.name);
    if (!name || name === item.name) return;
    setError(null);
    try {
      await rename.mutateAsync({ id: item.id, name });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Rename failed");
    }
  }

  const menuItem = menu ? visibleItems.find((i) => i.id === menu.id) ?? null : null;

  function enqueueChannelFiles(files: File[]) {
    uploads.enqueueChannel(files, teamSlug, channelName, folderId);
    void qc.invalidateQueries({ queryKey: listKey });
  }

  const { dropActive } = useFileDrop({
    enabled: !trash,
    onFiles: enqueueChannelFiles,
  });

  function idsForDrag(item: FileItem): string[] {
    if (checked.has(item.id) && checked.size > 1) {
      return checkedItems.filter(itemMovable).map((i) => i.id);
    }
    return itemMovable(item) ? [item.id] : [];
  }

  function onRowDragStart(e: DragEvent, item: FileItem) {
    const ids = idsForDrag(item);
    if (ids.length === 0) {
      e.preventDefault();
      return;
    }
    setDraggedItemIds(ids);
    e.dataTransfer.setData(CHANNELED_ITEMS_MIME, JSON.stringify(ids));
    e.dataTransfer.effectAllowed = "move";
    if (ids.length > 1) {
      const ghost = document.createElement("div");
      ghost.textContent = `${ids.length} items`;
      ghost.className = styles.dragGhost;
      document.body.appendChild(ghost);
      e.dataTransfer.setDragImage(ghost, 12, 12);
      requestAnimationFrame(() => ghost.remove());
    }
  }

  function onRowDragEnd() {
    clearDraggedItemIds();
    dropDepth.current = 0;
    setDropTargetId(null);
  }

  function bindFolderDrop(targetFolderId: string) {
    return {
      onDragEnter: (e: DragEvent) => {
        if (getDraggedItemIds().length === 0) return;
        e.preventDefault();
        e.stopPropagation();
        dropDepth.current += 1;
        setDropTargetId(targetFolderId);
      },
      onDragOver: (e: DragEvent) => {
        if (getDraggedItemIds().length === 0) return;
        e.preventDefault();
        e.stopPropagation();
        e.dataTransfer.dropEffect = "move";
      },
      onDragLeave: (e: DragEvent) => {
        if (getDraggedItemIds().length === 0) return;
        e.stopPropagation();
        dropDepth.current = Math.max(0, dropDepth.current - 1);
        if (dropDepth.current === 0) setDropTargetId(null);
      },
      onDrop: (e: DragEvent) => {
        if (getDraggedItemIds().length === 0) return;
        e.preventDefault();
        e.stopPropagation();
        dropDepth.current = 0;
        setDropTargetId(null);
        void dropOntoFolder(targetFolderId);
      },
    };
  }

  async function dropOntoFolder(targetFolderId: string | null) {
    const ids = getDraggedItemIds().filter((id) => id !== targetFolderId);
    clearDraggedItemIds();
    if (ids.length === 0) return;
    const byId = new Map(visibleItems.map((i) => [i.id, i]));
    // Also use checked items for previousFolderId; current folder is source
    const items: MoveBatchPrior[] = ids.map((id) => {
      const item = byId.get(id);
      return {
        id,
        previousFolderId: item?.folderId ?? folderId ?? null,
        name: item?.name,
      };
    });
    await runMove(items, targetFolderId);
  }

  async function applyPickerMove(targetFolderId: string | null) {
    const sourceItems = singleMoveItem
      ? [singleMoveItem]
      : movableChecked.length > 0
        ? movableChecked
        : checkedItems;
    const items: MoveBatchPrior[] = sourceItems.map((i) => ({
      id: i.id,
      previousFolderId: i.folderId ?? folderId ?? null,
      name: i.name,
    }));
    await runMove(items, targetFolderId);
    setMoveOpen(false);
    setSingleMoveItem(null);
  }

  return (
    <AppShell
      title={trash ? `Trash · #${channelName}` : `#${channelName}`}
      sidebar={<ChannelSidebar teamSlug={teamSlug} channelName={channelName} trash={trash} />}
      details={showPreview ? details : undefined}
      topbarExtra={
        <FilesSearchField value={nameQuery} onChange={setNameQuery} placeholder="Filter files…" />
      }
    >
      <DropOverlay active={dropActive} label={`Drop files to upload to #${channelName}`} />
      <div className={styles.headingBar} data-testid="dropzone">
        <div>
          <div className={styles.headingRow}>
            <h2 className={styles.heading}>
              {trash ? "Trash" : (currentFolderName ?? channelName)}
            </h2>
            {!trash && <span className={styles.pill}>File channel</span>}
          </div>
          {!trash && (
            <FolderBreadcrumbs
              rootLabel={channelName}
              folderId={folderId}
              folderNameHint={folderNameHint}
              path={path}
              onNavigate={goToSegment}
              onDropItems={(id) => void dropOntoFolder(id)}
              dropActiveId={dropTargetId}
              onDropTargetChange={setDropTargetId}
            />
          )}
          <p className={styles.lede}>
            {trash
              ? `Items removed from #${channelName}`
              : folderId
                ? `Contents of this folder in #${channelName}`
                : `Files in #${channelName}`}
          </p>
        </div>
        <div className={styles.toolbarActions}>
          {!trash && (
            <>
              <SelectionControls
                hasItems={visibleItems.length > 0}
                disabled={bulkBusy || creatingFolder}
                onSelectAll={selectAllVisible}
                onSelectNone={selectNone}
                onInvert={invertVisible}
              />
              <Button
                type="button"
                data-testid="new-folder"
                onClick={() => {
                  setCreateFolderError(null);
                  setCreateFolderOpen(true);
                }}
              >
                New folder
              </Button>
              {!isTablet && (
                <Button
                  type="button"
                  data-testid="toggle-preview"
                  aria-pressed={previewOpen}
                  title={previewOpen ? "Hide preview" : "Show preview"}
                  onClick={() => setPreviewOpen((v) => !v)}
                >
                  {previewOpen ? "Hide preview" : "Preview"}
                </Button>
              )}
              <UploadButton testId="file-input" onFiles={enqueueChannelFiles} />
            </>
          )}
        </div>
      </div>

      {folderUnavailable && (
        <p className={styles.error} role="status" data-testid="folder-unavailable">
          Folder unavailable — showing root.
        </p>
      )}

      {moveProgress && <MoveProgress done={moveProgress.done} total={moveProgress.total} />}
      {moveSummary && (
        <MoveSummary message={moveSummary.message} failures={moveSummary.failures} />
      )}

      {!trash && (
        <BulkBar
          count={checked.size}
          busy={bulkBusy || creatingFolder}
          downloadEnabled={visibleItems.some((i) => checked.has(i.id) && i.kind === ItemKind.File)}
          moveEnabled={movableChecked.length > 0}
          onClear={selectNone}
          onDownload={downloadChecked}
          onNewFolder={() => {
            setCreateFolderError(null);
            setCreateFolderOpen(true);
          }}
          onMoveTo={() => {
            setBulkError(null);
            setSingleMoveItem(null);
            setMoveOpen(true);
          }}
          onSendToChannel={() => {
            setBulkError(null);
            setSendOpen(true);
          }}
        />
      )}

      {error && <p className={styles.error}>{error}</p>}
      {list.isLoading && <p>Loading…</p>}
      {list.isError && !(list.error instanceof ApiError && list.error.status === 404) && (
        <p className={styles.error}>Failed to load items.</p>
      )}
      {!list.isLoading && (list.data?.items.length ?? 0) === 0 && (
        <p data-testid="empty-state">
          {folderId ? "This folder is empty" : `No files in #${channelName} yet`}
        </p>
      )}
      {!list.isLoading && (list.data?.items.length ?? 0) > 0 && visibleItems.length === 0 && (
        <p data-testid="filter-empty">No items match these filters</p>
      )}

      {(list.data?.items.length ?? 0) > 0 && (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="file-table">
            <thead>
              <tr>
                <th scope="col">
                  {!trash && (
                    <input
                      type="checkbox"
                      className={styles.check}
                      data-testid="select-all"
                      checked={allVisibleChecked}
                      ref={(el) => {
                        if (el) el.indeterminate = someVisibleChecked;
                      }}
                      aria-label="Select all"
                      onChange={toggleAllVisible}
                    />
                  )}
                </th>
                <SortableTh
                  column="name"
                  label="Name"
                  sort={sort}
                  sortDir={sortDir}
                  onSort={onSortColumn}
                />
                <SortableTh
                  column="type"
                  label="Type"
                  sort={sort}
                  sortDir={sortDir}
                  onSort={onSortColumn}
                />
                <SortableTh
                  column="size"
                  label="Size"
                  sort={sort}
                  sortDir={sortDir}
                  onSort={onSortColumn}
                />
                <SortableTh
                  column="updated"
                  label="Modified"
                  sort={sort}
                  sortDir={sortDir}
                  onSort={onSortColumn}
                />
                <th scope="col">
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {visibleItems.map((item) => {
                const movable = !trash && itemMovable(item);
                const isFolder = item.kind === ItemKind.Folder;
                return (
                  <tr
                    key={item.id}
                    data-testid={`row-${item.name}`}
                    data-selected={
                      selected?.id === item.id || checked.has(item.id) ? "true" : "false"
                    }
                    data-drop-active={
                      isFolder && dropTargetId === item.id ? "true" : undefined
                    }
                    draggable={movable}
                    onDragStart={(e) => onRowDragStart(e, item)}
                    onDragEnd={onRowDragEnd}
                    onClick={() => setSelected(item)}
                    {...(isFolder && !trash ? bindFolderDrop(item.id) : {})}
                  >
                    <td onClick={(e) => e.stopPropagation()}>
                      {!trash && (
                        <input
                          type="checkbox"
                          className={styles.check}
                          data-testid={`check-${item.name}`}
                          checked={checked.has(item.id)}
                          aria-label={`Select ${item.name}`}
                          onChange={() => toggleChecked(item.id)}
                          onMouseDown={(e) => e.stopPropagation()}
                        />
                      )}
                    </td>
                    <td>
                      <div className={styles.nameCell}>
                        {isFolder && !trash ? (
                          <span className={styles.folderGlyph} aria-hidden="true">
                            ▦
                          </span>
                        ) : (
                          <FileThumb item={item} contentUrl={contentUrlFor(item)} />
                        )}
                        <div className={styles.nameText}>
                          {isFolder ? (
                            <button
                              type="button"
                              className={styles.linkish}
                              data-testid={`open-folder-${item.name}`}
                              onClick={(e) => {
                                e.stopPropagation();
                                openFolder(item.id, item.name);
                              }}
                              onDoubleClick={(e) => {
                                e.stopPropagation();
                                openFolder(item.id, item.name);
                              }}
                              onMouseDown={(e) => e.stopPropagation()}
                            >
                              {item.name}
                            </button>
                          ) : (
                            <span className={styles.linkish}>{item.name}</span>
                          )}
                        </div>
                      </div>
                    </td>
                    <td>
                      <span className={styles.typeCell}>{formatFileType(item)}</span>
                    </td>
                    <td>{isFolder ? "—" : formatBytes(item.sizeBytes)}</td>
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
                          aria-expanded={menu?.id === item.id}
                          data-open={menu?.id === item.id ? "true" : undefined}
                          data-menu-trigger={item.id}
                          data-testid={`actions-${item.name}`}
                          onClick={(e) => {
                            e.stopPropagation();
                            openMenu(item.id, e.currentTarget);
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
              })}
            </tbody>
          </table>
        </div>
      )}
      {!trash && (
        <div className={styles.dropHint}>Drop files anywhere on this page to upload to #{channelName}</div>
      )}

      <UndoToast toast={undoToast} onDismiss={() => setUndoToast(null)} />

      <ShareLinkDialog
        open={shareItem !== null}
        teamSlug={teamSlug}
        channelName={channelName}
        fileId={shareItem?.id ?? null}
        fileName={shareItem?.name ?? ""}
        onClose={() => setShareItem(null)}
      />

      <CreateFolderDialog
        open={createFolderOpen}
        busy={creatingFolder}
        error={createFolderError}
        description={
          checked.size > 0
            ? `Create a folder and move ${checked.size} selected item${checked.size === 1 ? "" : "s"} into it.`
            : null
        }
        onClose={() => {
          if (creatingFolder) return;
          setCreateFolderOpen(false);
          setCreateFolderError(null);
        }}
        onCreate={async (name) => {
          setCreatingFolder(true);
          setCreateFolderError(null);
          try {
            const folder = await filesApi.createFolder(teamSlug, channelName, name, folderId ?? null);
            if (movableChecked.length > 0) {
              await runMove(
                movableChecked.map((i) => ({
                  id: i.id,
                  previousFolderId: i.folderId ?? folderId ?? null,
                  name: i.name,
                })),
                folder.id,
              );
            }
            setCreateFolderOpen(false);
            void qc.invalidateQueries({ queryKey: listKey });
          } catch (e) {
            setCreateFolderError(e instanceof Error ? e.message : "Could not create folder.");
          } finally {
            setCreatingFolder(false);
          }
        }}
      />

      <PickListDialog
        open={moveOpen || singleMoveItem !== null}
        title={
          singleMoveItem
            ? `Move “${singleMoveItem.name}”`
            : `Move ${movableChecked.length || checked.size} item${(movableChecked.length || checked.size) === 1 ? "" : "s"}`
        }
        testId="move-to-dialog"
        options={movePickerOptions}
        allowNone
        noneLabel="Channel root"
        busy={bulkBusy}
        error={bulkError}
        confirmLabel="Move"
        onClose={() => {
          if (bulkBusy) return;
          setMoveOpen(false);
          setSingleMoveItem(null);
          setBulkError(null);
        }}
        onPick={async (targetFolderId) => {
          try {
            await applyPickerMove(targetFolderId);
          } catch (e) {
            setBulkError(e instanceof Error ? e.message : "Move failed");
          }
        }}
      />

      <PickListDialog
        open={sendOpen}
        title={`Send ${checkedItems.filter((i) => i.kind === ItemKind.File).length || checked.size} file${checked.size === 1 ? "" : "s"} to channel`}
        testId="send-channel-dialog"
        options={sendChannelOptions}
        emptyLabel="No other file channels in this team"
        busy={bulkBusy}
        error={bulkError}
        confirmLabel="Send"
        onClose={() => {
          if (bulkBusy) return;
          setSendOpen(false);
          setBulkError(null);
        }}
        onPick={async (targetChannelName) => {
          if (!targetChannelName) return;
          const files = checkedItems.filter((i) => i.kind === ItemKind.File && itemMovable(i));
          if (files.length === 0) {
            setBulkError("Only files you can move can be sent to another channel.");
            return;
          }
          setSendOpen(false);
          await runMove(
            files.map((i) => ({
              id: i.id,
              previousFolderId: i.folderId ?? folderId ?? null,
              name: i.name,
            })),
            null,
            targetChannelName,
            { undoable: false },
          );
        }}
      />
      {menuItem &&
        createPortal(
          <ul
            ref={menuRef}
            className={styles.menu}
            role="menu"
            style={{ top: menu!.top, left: menu!.left }}
            data-testid={`actions-menu-${menuItem.name}`}
          >
            {!trash && (
              <>
                <li>
                  <button
                    type="button"
                    role="menuitem"
                    onClick={() => {
                      closeMenu();
                      void onRename(menuItem);
                    }}
                  >
                    Rename
                  </button>
                </li>
                {itemMovable(menuItem) && (
                  <li>
                    <button
                      type="button"
                      role="menuitem"
                      data-testid={`move-${menuItem.name}`}
                      onClick={() => {
                        closeMenu();
                        setBulkError(null);
                        setSingleMoveItem(menuItem);
                      }}
                    >
                      Move
                    </button>
                  </li>
                )}
                {menuItem.kind === ItemKind.File && (
                  <>
                    <li>
                      <button
                        type="button"
                        role="menuitem"
                        data-testid={`download-${menuItem.name}`}
                        onClick={() => {
                          closeMenu();
                          void downloadItem(menuItem);
                        }}
                      >
                        Download
                      </button>
                    </li>
                    <li>
                      <button
                        type="button"
                        role="menuitem"
                        data-testid={`share-${menuItem.name}`}
                        onClick={() => {
                          closeMenu();
                          setShareItem(menuItem);
                        }}
                      >
                        Public link…
                      </button>
                    </li>
                  </>
                )}
                <li>
                  <button
                    type="button"
                    role="menuitem"
                    data-testid={`trash-${menuItem.name}`}
                    onClick={() => {
                      closeMenu();
                      void trashMut.mutateAsync(menuItem.id);
                    }}
                  >
                    Move to trash
                  </button>
                </li>
              </>
            )}
            {trash && (
              <li>
                <button
                  type="button"
                  role="menuitem"
                  data-testid={`restore-${menuItem.name}`}
                  onClick={() => {
                    closeMenu();
                    void restore.mutateAsync(menuItem.id);
                  }}
                >
                  Restore
                </button>
              </li>
            )}
          </ul>,
          document.body,
        )}
    </AppShell>
  );
}
