import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useCallback, useEffect, useMemo, useRef, useState, type DragEvent } from "react";
import { createPortal } from "react-dom";
import {
  ChannelKind,
  ItemKind,
  personalApi,
  teamsApi,
  type FileItem,
  type FolderPathSegment,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { AppShell } from "../../components/AppShell";
import { UserChip } from "../../components/UserChip";
import { formatFileType } from "../../lib/describeFileKind";
import { formatBytes } from "../../lib/formatBytes";
import { ModifiedAt } from "../../lib/formatModified";
import { triggerDownload, triggerDownloads } from "../../lib/triggerDownload";
import { TABLET_MQ, useMediaQuery } from "../../lib/useMediaQuery";
import { DropOverlay } from "../../uploads/DropOverlay";
import { useFileDrop } from "../../uploads/useFileDrop";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { BulkBar } from "../files/BulkBar";
import { CreateFolderDialog } from "../files/CreateFolderDialog";
import {
  CHANNELED_ITEMS_MIME,
  clearDraggedItemIds,
  getDraggedItemIds,
  setDraggedItemIds,
} from "../files/dragItems";
import { FileThumb } from "../files/FileThumb";
import { FilePreviewPanel } from "../files/FilePreviewPanel";
import { FilesSearchField } from "../files/FilesSearchField";
import { FolderBreadcrumbs } from "../files/FolderBreadcrumbs";
import {
  MoveProgress,
  MoveSummary,
  UndoToast,
  type UndoToastState,
} from "../files/MoveFeedback";
import { moveBatch, summarizeMoveResult, type MoveBatchPrior } from "../files/moveBatch";
import { PickListDialog } from "../files/PickListDialog";
import { SelectionControls } from "../files/SelectionControls";
import { nextSortState, SortableTh, type SortColumn } from "../files/SortableTh";
import { UploadButton } from "../files/UploadButton";
import { useFolderNav } from "../files/useFolderNav";
import styles from "../files/FileBrowser.module.css";
import { PersonalSidebar } from "./PersonalSidebar";

type Mode = "files" | "recent" | "starred" | "trash";
type MenuState = { id: string; top: number; left: number };

const titles: Record<Mode, string> = {
  files: "All files",
  recent: "Recent",
  starred: "Starred",
  trash: "Trash",
};

export function PersonalFilesPage({ mode = "files" }: { mode?: Mode }) {
  const qc = useQueryClient();
  const uploads = useUploads();
  const trash = mode === "trash";
  const canBrowseFolders = mode === "files";
  const canUpload = !trash && mode === "files";
  const canBulk = !trash && mode === "files";

  const { folderId, folderNameHint, openFolder, goToSegment, clearInvalidFolder, goToRoot } =
    useFolderNav(canBrowseFolders);

  useEffect(() => {
    if (!canBrowseFolders) {
      goToRoot({ replace: true });
    }
  }, [canBrowseFolders, goToRoot]);

  const [selected, setSelected] = useState<FileItem | null>(null);
  const isTablet = useMediaQuery(TABLET_MQ);
  const [previewOpen, setPreviewOpen] = useState(() => {
    try {
      return localStorage.getItem("channeled.previewOpen") !== "0";
    } catch {
      return true;
    }
  });
  const showPreview = previewOpen && !isTablet;

  useEffect(() => {
    try {
      localStorage.setItem("channeled.previewOpen", previewOpen ? "1" : "0");
    } catch {
      /* ignore */
    }
  }, [previewOpen]);
  const [createFolderOpen, setCreateFolderOpen] = useState(false);
  const [createFolderError, setCreateFolderError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [folderUnavailable, setFolderUnavailable] = useState(false);
  const [nameQuery, setNameQuery] = useState("");
  const [sort, setSort] = useState<SortColumn>("name");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [menu, setMenu] = useState<MenuState | null>(null);
  const [copySuccess, setCopySuccess] = useState(false);
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
  const menuRef = useRef<HTMLUListElement | null>(null);
  const dropDepth = useRef(0);

  useEffect(() => {
    setChecked(new Set());
    setSelected(null);
    setMoveSummary(null);
  }, [folderId, mode]);

  useEffect(() => {
    if (!menu) return;
    const close = () => setMenu(null);
    const onPointerDown = (e: PointerEvent) => {
      const target = e.target as Node;
      if (menuRef.current?.contains(target)) return;
      if ((target as Element).closest?.(`[data-menu-trigger="${menu.id}"]`)) return;
      close();
    };
    const scrollTimer = window.setTimeout(() => {
      window.addEventListener("scroll", close, true);
    }, 100);
    window.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("resize", close);
    return () => {
      window.clearTimeout(scrollTimer);
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

  const listKey = useMemo(
    () => ["personal", mode, canBrowseFolders ? folderId : null] as const,
    [mode, canBrowseFolders, folderId],
  );

  const list = useQuery({
    queryKey: listKey,
    queryFn: async () => {
      if (mode === "recent") return personalApi.recent();
      if (mode === "starred") return personalApi.starred();
      return personalApi.list({ trash, folderId: canBrowseFolders ? folderId : undefined });
    },
    retry: (count, err) => {
      if (err instanceof ApiError && err.status === 404) return false;
      return count < 2;
    },
  });

  useEffect(() => {
    if (!folderId || !canBrowseFolders) return;
    if (list.error instanceof ApiError && list.error.status === 404) {
      clearInvalidFolder();
      setFolderUnavailable(true);
    }
  }, [list.error, folderId, canBrowseFolders, clearInvalidFolder]);

  useEffect(() => {
    if (list.isSuccess && folderId) setFolderUnavailable(false);
  }, [list.isSuccess, folderId]);

  function enqueue(files: File[]) {
    uploads.enqueuePersonal(files, folderId);
  }

  const { dropActive } = useFileDrop({
    enabled: canUpload,
    onFiles: enqueue,
  });

  const seenDone = useRef(new Set<string>());
  useEffect(() => {
    let refreshed = false;
    for (const job of uploads.jobs) {
      if (job.status === "done" && job.scope === "personal" && !seenDone.current.has(job.id)) {
        seenDone.current.add(job.id);
        refreshed = true;
      }
    }
    if (refreshed) {
      void qc.refetchQueries({ queryKey: ["personal"], type: "active" });
    }
  }, [uploads.jobs, qc]);

  const teams = useQuery({ queryKey: ["teams"], queryFn: () => teamsApi.list() });
  const primaryTeam = teams.data?.[0];
  const teamChannels = useQuery({
    queryKey: ["channels", primaryTeam?.slug],
    queryFn: () => teamsApi.channels(primaryTeam!.slug),
    enabled: !!primaryTeam?.slug,
  });

  const copy = useMutation({
    mutationFn: (item: FileItem) => {
      if (!primaryTeam) throw new Error("Create a team first");
      return personalApi.copyToChannel(item.id, primaryTeam.slug, "general", item.name);
    },
    onSuccess: () => setCopySuccess(true),
  });

  const foldersHere = useMemo(
    () => (list.data?.items ?? []).filter((i) => i.kind === ItemKind.Folder),
    [list.data?.items],
  );
  const path =
    list.data?.path ?? (list.data as { Path?: FolderPathSegment[] } | undefined)?.Path ?? null;

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

  const checkedItems = useMemo(
    () => visibleItems.filter((i) => checked.has(i.id)),
    [visibleItems, checked],
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
    const siblings = foldersHere
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
  }, [path, folderId, foldersHere, checked, singleMoveItem]);

  const sendChannelOptions = useMemo(
    () =>
      (teamChannels.data ?? [])
        .filter((c) => (c.kind ?? ChannelKind.File) === ChannelKind.File)
        .map((c) => ({
          id: c.name,
          label: primaryTeam ? `${primaryTeam.name} · #${c.name}` : `#${c.name}`,
        })),
    [teamChannels.data, primaryTeam],
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

  const runMove = useCallback(
    async (items: MoveBatchPrior[], targetFolderId: string | null) => {
      if (items.length === 0) return;
      setBulkBusy(true);
      setBulkError(null);
      setMoveProgress({ done: 0, total: items.length });
      setMoveSummary(null);
      try {
        const result = await moveBatch({
          items,
          targetFolderId,
          moveOne: (id, folder) => personalApi.move(id, folder),
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
        void qc.invalidateQueries({ queryKey: ["personal"] });
        if (result.successes.length > 0) {
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
                      await personalApi.move(id, prior.previousFolderId);
                    },
                    onProgress: (done, total) => setMoveProgress({ done, total }),
                  });
                  setMoveSummary({
                    message: summarizeMoveResult(undoResult).replace(/^Moved/, "Restored"),
                    failures: undoResult.failures,
                  });
                  void qc.invalidateQueries({ queryKey: ["personal"] });
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
    [qc],
  );

  async function onRename(item: FileItem) {
    const name = window.prompt("Rename to", item.name);
    if (!name || name === item.name) return;
    setError(null);
    try {
      await personalApi.rename(item.id, name.trim());
      void qc.invalidateQueries({ queryKey: ["personal"] });
    } catch (e) {
      setError(e instanceof Error ? e.message : "Rename failed");
    }
  }

  const menuItem = menu ? visibleItems.find((i) => i.id === menu.id) ?? null : null;
  const currentFolderName = path?.at(-1)?.name ?? folderNameHint ?? undefined;
  const heading = mode === "files" && currentFolderName ? currentFolderName : titles[mode];
  const rawCount = list.data?.items.length ?? 0;

  function contentUrlFor(item: FileItem, opts?: { download?: boolean }) {
    if (item.kind === ItemKind.Folder) return null;
    return personalApi.contentUrl(item.id, opts);
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
        url: personalApi.contentUrl(i.id, { download: true }),
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

  function idsForDrag(item: FileItem): string[] {
    if (checked.has(item.id) && checked.size > 1) {
      return checkedItems.map((i) => i.id);
    }
    return [item.id];
  }

  function onRowDragStart(e: DragEvent, item: FileItem) {
    if (!canBulk) {
      e.preventDefault();
      return;
    }
    const ids = idsForDrag(item);
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
    const sourceItems = singleMoveItem ? [singleMoveItem] : checkedItems;
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
      title={trash ? "Trash · My files" : "My files"}
      sidebar={<PersonalSidebar />}
      details={showPreview ? details : undefined}
      topbarExtra={
        <FilesSearchField value={nameQuery} onChange={setNameQuery} placeholder="Filter files…" />
      }
    >
      <DropOverlay active={dropActive} label="Drop files to upload to My files" />

      <div className={styles.headingBar} data-testid="dropzone">
        <div>
          <div className={styles.headingRow}>
            <h2 className={styles.heading}>{heading}</h2>
            {!trash && <span className={styles.pill}>Personal</span>}
          </div>
          {canBrowseFolders && (
            <FolderBreadcrumbs
              rootLabel="My files"
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
            {trash ? "Items in your personal trash" : "Your private files"}
          </p>
        </div>
        <div className={styles.toolbarActions}>
          {canBulk && (
            <SelectionControls
              hasItems={visibleItems.length > 0}
              disabled={bulkBusy || creatingFolder}
              onSelectAll={selectAllVisible}
              onSelectNone={selectNone}
              onInvert={invertVisible}
            />
          )}
          {canUpload && (
            <>
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
              <UploadButton testId="personal-file-input" onFiles={enqueue} />
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

      {canBulk && (
        <BulkBar
          count={checked.size}
          busy={bulkBusy || creatingFolder}
          downloadEnabled={visibleItems.some((i) => checked.has(i.id) && i.kind === ItemKind.File)}
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

      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}

      {list.isLoading && <p>Loading…</p>}
      {!list.isLoading && rawCount === 0 && (
        <p data-testid="personal-empty">
          {canBrowseFolders && folderId
            ? "This folder is empty"
            : canUpload
              ? "No files yet"
              : "No items here."}
        </p>
      )}
      {!list.isLoading && rawCount > 0 && visibleItems.length === 0 && (
        <p>No items match these filters</p>
      )}

      {rawCount > 0 && (
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="personal-table">
            <thead>
              <tr>
                <th scope="col">
                  {canBulk && (
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
                <th scope="col">Uploaded by</th>
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
                const isFolder = item.kind === ItemKind.Folder;
                return (
                  <tr
                    key={item.id}
                    data-testid={`personal-row-${item.name}`}
                    data-selected={
                      selected?.id === item.id || checked.has(item.id) ? "true" : "false"
                    }
                    data-drop-active={
                      isFolder && dropTargetId === item.id ? "true" : undefined
                    }
                    draggable={canBulk}
                    onDragStart={(e) => onRowDragStart(e, item)}
                    onDragEnd={onRowDragEnd}
                    onClick={() => setSelected(item)}
                    {...(isFolder && canBrowseFolders ? bindFolderDrop(item.id) : {})}
                  >
                    <td onClick={(e) => e.stopPropagation()}>
                      {canBulk && (
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
                        {isFolder && canBrowseFolders ? (
                          <span className={styles.folderGlyph} aria-hidden="true">
                            ▦
                          </span>
                        ) : (
                          <FileThumb item={item} contentUrl={contentUrlFor(item)} />
                        )}
                        <div className={styles.nameText}>
                          {isFolder && canBrowseFolders && !trash ? (
                            <button
                              type="button"
                              className={styles.linkish}
                              data-testid={`personal-open-folder-${item.name}`}
                              onClick={(e) => {
                                e.stopPropagation();
                                openFolder(item.id, item.name);
                              }}
                              onMouseDown={(e) => e.stopPropagation()}
                            >
                              {item.name}
                            </button>
                          ) : (
                            <span className={styles.linkish}>
                              {isFolder ? `${item.name}/` : item.name}
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

      {canUpload && (
        <div className={styles.dropHint}>Drop files anywhere on this page to upload to My files</div>
      )}

      {copySuccess && <p data-testid="copy-success">Copied to team channel.</p>}

      <UndoToast toast={undoToast} onDismiss={() => setUndoToast(null)} />

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
            const folder = await personalApi.createFolder(name, folderId ?? null);
            if (checked.size > 0) {
              await runMove(
                checkedItems.map((i) => ({
                  id: i.id,
                  previousFolderId: i.folderId ?? folderId ?? null,
                  name: i.name,
                })),
                folder.id,
              );
            }
            setCreateFolderOpen(false);
            void qc.invalidateQueries({ queryKey: ["personal"] });
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
            : `Move ${checked.size} item${checked.size === 1 ? "" : "s"}`
        }
        testId="move-to-dialog"
        options={movePickerOptions}
        allowNone
        noneLabel="My files root"
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
        title={`Send files to channel`}
        testId="send-channel-dialog"
        options={sendChannelOptions}
        description="Choose a team file channel. A copy of each selected file is sent there."
        emptyLabel={primaryTeam ? "No file channels in your team" : "Create a team first"}
        busy={bulkBusy}
        error={bulkError}
        confirmLabel="Send"
        onClose={() => {
          if (bulkBusy) return;
          setSendOpen(false);
          setBulkError(null);
        }}
        onPick={async (channelName) => {
          if (!channelName || !primaryTeam) return;
          const files = checkedItems.filter((i) => i.kind === ItemKind.File);
          if (files.length === 0) {
            setBulkError("Only files can be sent to a channel.");
            return;
          }
          setBulkBusy(true);
          setBulkError(null);
          try {
            for (const file of files) {
              await personalApi.copyToChannel(file.id, primaryTeam.slug, channelName, file.name);
            }
            setChecked(new Set());
            setSendOpen(false);
            setCopySuccess(true);
            void qc.invalidateQueries({ queryKey: ["personal"] });
          } catch (e) {
            setBulkError(e instanceof Error ? e.message : "Send failed");
          } finally {
            setBulkBusy(false);
          }
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
                {canBrowseFolders && (
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
                  </>
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
                          downloadItem(menuItem);
                        }}
                      >
                        Download
                      </button>
                    </li>
                    <li>
                      <button
                        type="button"
                        role="menuitem"
                        data-testid={`copy-${menuItem.name}`}
                        onClick={() => {
                          closeMenu();
                          void copy.mutateAsync(menuItem);
                        }}
                      >
                        Copy to channel
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
                      void personalApi.trash(menuItem.id).then(() =>
                        qc.invalidateQueries({ queryKey: ["personal"] }),
                      );
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
                    void personalApi.restore(menuItem.id).then(() =>
                      qc.invalidateQueries({ queryKey: ["personal"] }),
                    );
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
