import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
  type DragEvent,
  type MouseEvent as ReactMouseEvent,
} from "react";
import { createPortal } from "react-dom";
import { useSearchParams } from "react-router-dom";
import {
  ChannelKind,
  filesApi,
  ItemKind,
  MemberRole,
  messagesApi,
  teamsApi,
  type FileItem,
  type FolderPathSegment,
} from "../../api/channeled";
import { ApiError } from "../../api/http";
import { useAuth } from "../../auth/AuthContext";
import { AppShell } from "../../components/AppShell";
import { useAnnounce } from "../../components/liveAnnouncerContext";
import { describeDownloadError, downloadItems, summarizeDownloadOutcome } from "../../lib/triggerDownload";
import { TABLET_MQ, useMediaQuery } from "../../lib/useMediaQuery";
import { useChannelFilesChanged } from "../../realtime/useChannelHub";
import { DropOverlay } from "../../uploads/DropOverlay";
import { useFileDrop } from "../../uploads/useFileDrop";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { SkeletonList } from "../../ui/SkeletonList";
import { ChannelSidebar } from "../teams/ChannelSidebar";
import { BulkBar } from "./BulkBar";
import { canMoveItem } from "./canMoveItem";
import { ClipboardChip } from "./ClipboardChip";
import { CreateFolderDialog } from "./CreateFolderDialog";
import { useFileClipboard } from "./useFileClipboard";
import {
  CHANNELED_ITEMS_MIME,
  clearDraggedItemIds,
  getDraggedItemIds,
  setDraggedItemIds,
} from "./dragItems";
import { FilePreviewPanel } from "./FilePreviewPanel";
import { FolderBreadcrumbs } from "./FolderBreadcrumbs";
import { PreviewToggle } from "./PreviewToggle";
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
import { FileRow, type FileRowHandlers } from "./FileRow";
import { mergePages, selectionLabel, sortParam } from "./pagedItems";
import { useStableCallback } from "../../lib/useStableCallback";
import { nextSortState, SortableTh, type SortColumn } from "./SortableTh";
import { useLocatedItem } from "./useLocatedItem";
import { usePagedItems } from "./usePagedItems";
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

  // Channel switches move focus to the page heading and announce the channel (not folder moves).
  const announce = useAnnounce();
  const headingRef = useRef<HTMLHeadingElement>(null);
  useEffect(() => {
    headingRef.current?.focus({ preventScroll: true });
    announce(trash ? `Trash, #${channelName}` : `#${channelName}, file channel`);
  }, [teamSlug, channelName, trash, announce]);

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
  const [searchParams, setSearchParams] = useSearchParams();
  const highlightItemId = searchParams.get("item");
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
  const [menu, setMenu] = useState<MenuState | null>(null);
  const menuRef = useRef<HTMLUListElement | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [folderUnavailable, setFolderUnavailable] = useState(false);
  const [createFolderOpen, setCreateFolderOpen] = useState(false);
  const [createFolderError, setCreateFolderError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [shareItem, setShareItem] = useState<FileItem | null>(null);
  const [shareChatItem, setShareChatItem] = useState<FileItem | null>(null);
  const [shareChatBusy, setShareChatBusy] = useState(false);
  const [shareChatError, setShareChatError] = useState<string | null>(null);
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
    // Defer scroll-close so opening the menu (and Playwright scrolling the trigger into view)
    // does not immediately dismiss it.
    const scrollTimer = window.setTimeout(() => {
      window.addEventListener("scroll", close, true);
    }, 100);
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") close();
    };
    window.addEventListener("pointerdown", onPointerDown);
    window.addEventListener("resize", close);
    window.addEventListener("keydown", onKey);
    return () => {
      window.clearTimeout(scrollTimer);
      window.removeEventListener("pointerdown", onPointerDown);
      window.removeEventListener("scroll", close, true);
      window.removeEventListener("resize", close);
      window.removeEventListener("keydown", onKey);
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

  function openContextMenu(itemId: string, e: ReactMouseEvent) {
    e.preventDefault();
    e.stopPropagation();
    const width = 160;
    const left = Math.min(Math.max(8, e.clientX), window.innerWidth - width - 8);
    const top = Math.min(Math.max(8, e.clientY), window.innerHeight - 8);
    setMenu({ id: itemId, top, left });
  }

  function closeMenu() {
    setMenu(null);
  }

  // Server-side sort and cursor paging; the client never re-sorts loaded rows.
  const serverSort = sortParam(sort, sortDir);
  const paged = usePagedItems(
    ["items", teamSlug, channelName, folderId, trash, serverSort],
    (cursor, signal) =>
      filesApi.list(teamSlug, channelName, { folderId, trash, sort: serverSort, cursor }, signal),
  );
  const list = paged.query;

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

  // Selection never survives a change of what is listed (folder, sort): no acting on unseen rows.
  useEffect(() => {
    setChecked(new Set());
    setSelected(null);
    setMoveSummary(null);
  }, [folderId, trash, teamSlug, channelName, serverSort]);

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
      const key = ["items", teamSlug, channelName] as const;
      void qc.refetchQueries({ queryKey: key, type: "active" });
      // Upload complete can race SignalR JoinChannel; nudge once more shortly after.
      window.setTimeout(() => {
        void qc.refetchQueries({ queryKey: key, type: "active" });
      }, 300);
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

  const visibleItems = paged.items;
  const folders = useMemo(
    () => visibleItems.filter((i) => i.kind === ItemKind.Folder && !i.deletedAt),
    [visibleItems],
  );

  const path: FolderPathSegment[] | null = paged.path ?? null;
  const currentFolderName = path?.at(-1)?.name ?? folderNameHint ?? undefined;
  const loadedCount = visibleItems.length;

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
  const listedIds = useMemo(() => visibleItems.map((i) => i.id), [visibleItems]);
  const selectedIds = useMemo(() => {
    if (checked.size > 0) return [...checked];
    return selected ? [selected.id] : [];
  }, [checked, selected]);
  const clipboard = useFileClipboard({
    currentSource: {
      scope: "channel",
      teamSlug,
      channelName,
      folderId: folderId ?? null,
    },
    listedIds,
    listedItems: visibleItems,
    selectedIds,
    listReady: list.isSuccess,
    enabled: !trash,
    onError: (message) => setError(message),
    onPasteResult: (res) => {
      setMoveSummary({
        message:
          res.failed === 0
            ? `Pasted ${res.succeeded} item${res.succeeded === 1 ? "" : "s"}`
            : `Pasted ${res.succeeded} of ${res.succeeded + res.failed}`,
        failures: res.results
          .filter((r) => r.status !== "succeeded")
          .map((r) => ({
            id: r.itemId,
            message: r.message ?? r.errorCode ?? r.status,
          })),
      });
      void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
    },
    refresh: () => {
      void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
    },
  });
  const movableChecked = useMemo(
    () => checkedItems.filter(itemMovable),
    [checkedItems, itemMovable],
  );
  const allVisibleChecked =
    visibleItems.length > 0 && visibleItems.every((i) => checked.has(i.id));
  const someVisibleChecked = checkedItems.length > 0 && !allVisibleChecked;

  // Deep links (`?item=`, from /open/:id): select the row even when it is not on a loaded page yet — it is
  // pinned above the list until a later page loads it.
  const [focusId, setFocusId] = useState<string | null>(null);
  useEffect(() => {
    setFocusId(null);
  }, [folderId, trash, teamSlug, channelName]);
  useEffect(() => {
    if (!highlightItemId) return;
    setFocusId(highlightItemId);
    const next = new URLSearchParams(searchParams);
    next.delete("item");
    setSearchParams(next, { replace: true });
  }, [highlightItemId, searchParams, setSearchParams]);
  const located = useLocatedItem({
    focusId,
    loadedItems: visibleItems,
    listReady: list.isSuccess,
    view: { scope: "channel", teamSlug, channelName, folderId: folderId ?? null, trash },
  });
  const pinned = located.pinned;
  const focusTarget = located.target;
  useEffect(() => {
    if (!focusTarget) return;
    setSelected(focusTarget);
    requestAnimationFrame(() => {
      const row = document.querySelector<HTMLElement>(`[data-item-id="${focusTarget.id}"]`);
      row?.scrollIntoView({ block: "nearest" });
      row?.focus({ preventScroll: true });
    });
  }, [focusTarget]);
  const rows = pinned ? [pinned, ...visibleItems] : visibleItems;

  async function loadMore() {
    const before = loadedCount;
    const result = await paged.loadMore();
    const after = mergePages(result.data?.pages).length;
    announce(`Loaded ${Math.max(0, after - before)} more items. ${after} shown.`);
  }

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

  const textChannelShareOptions = useMemo(
    () =>
      (channels.data ?? [])
        .filter((c) => (c.kind ?? ChannelKind.File) === ChannelKind.Text)
        .map((c) => ({ id: c.name, label: `#${c.name}` })),
    [channels.data],
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

  async function download(items: FileItem[]) {
    const files = items.filter((i) => i.kind === ItemKind.File);
    if (files.length === 0) return;
    try {
      setError(null);
      const outcome = await downloadItems(
        files.map((i) => ({ scope: "channel" as const, teamSlug, channelName, id: i.id, name: i.name })),
      );
      setError(summarizeDownloadOutcome(outcome));
    } catch (e) {
      setError(describeDownloadError(e));
    }
  }

  function downloadItem(item: FileItem) {
    void download([item]);
  }

  function downloadChecked() {
    void download(visibleItems.filter((i) => checked.has(i.id)));
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

  const menuItem = menu ? rows.find((i) => i.id === menu.id) ?? null : null;

  // Stable row callbacks: memoized FileRows re-render only when their own props change.
  const onSelectRow = useStableCallback((item: FileItem) => setSelected(item));
  const onToggleRow = useStableCallback((id: string) => toggleChecked(id));
  const onOpenFolderRow = useStableCallback((item: FileItem) => openFolder(item.id, item.name));
  const onOpenMenuRow = useStableCallback((id: string, trigger: HTMLElement) => openMenu(id, trigger));
  const onContextMenuRow = useStableCallback((id: string, e: ReactMouseEvent) => openContextMenu(id, e));
  const onDragStartRow = useStableCallback((e: DragEvent, item: FileItem) => onRowDragStart(e, item));
  const onDragEndRow = useStableCallback(() => onRowDragEnd());
  const folderDropRow = useStableCallback((id: string) => bindFolderDrop(id));
  const rowHandlers = useMemo<FileRowHandlers>(
    () => ({
      onSelect: onSelectRow,
      onToggleCheck: onToggleRow,
      onOpenFolder: onOpenFolderRow,
      onOpenMenu: onOpenMenuRow,
      onContextMenu: onContextMenuRow,
      onDragStart: onDragStartRow,
      onDragEnd: onDragEndRow,
      folderDrop: folderDropRow,
    }),
    [onSelectRow, onToggleRow, onOpenFolderRow, onOpenMenuRow, onContextMenuRow, onDragStartRow, onDragEndRow, folderDropRow],
  );

  function enqueueChannelFiles(files: File[]) {
    uploads.enqueueChannel(files, teamSlug, channelName, folderId);
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
        !isTablet ? (
          <PreviewToggle open={previewOpen} onToggle={() => setPreviewOpen((v) => !v)} />
        ) : undefined
      }
      contentKey={`files:${teamSlug}/${channelName}${trash ? "/trash" : ""}`}
    >
      <DropOverlay active={dropActive} label={`Drop files to upload to #${channelName}`} />
      <div className={styles.headingBar} data-testid="dropzone">
        <div className={styles.headingMeta}>
          <div className={styles.headingRow}>
            <h2 className={styles.heading} ref={headingRef} tabIndex={-1}>
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
                ? `In #${channelName}`
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
        <ClipboardChip onPaste={() => void clipboard.paste()} pasteDisabled={bulkBusy} />
      )}

      {!trash && (
        <BulkBar
          count={checked.size}
          label={selectionLabel(checked.size, paged.hasMore)}
          busy={bulkBusy || creatingFolder}
          downloadEnabled={visibleItems.some((i) => checked.has(i.id) && i.kind === ItemKind.File)}
          moveEnabled={movableChecked.length > 0}
          onCut={() => clipboard.cutOrCopy("cut")}
          onCopy={() => clipboard.cutOrCopy("copy")}
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
        <p className={styles.error} role="alert" data-testid="browser-error">
          {error}
        </p>
      )}
      {list.isLoading && <SkeletonList rows={8} />}
      {list.isError && !(list.error instanceof ApiError && list.error.status === 404) && (
        <p className={styles.error}>Failed to load items.</p>
      )}
      {list.isSuccess && rows.length === 0 && (
        <div data-testid="empty-state">
          <p>{folderId ? "This folder is empty" : `No files in #${channelName} yet`}</p>
          {!trash && clipboard.clip && (
            <Button
              type="button"
              data-testid="empty-paste"
              onClick={() => void clipboard.paste()}
            >
              Paste
            </Button>
          )}
        </div>
      )}
      {rows.length > 0 && (
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
              {rows.map((item) => (
                <FileRow
                  key={item.id}
                  item={item}
                  rowTestIdPrefix="row"
                  openFolderTestIdPrefix="open-folder"
                  contentUrl={contentUrlFor(item)}
                  selected={selected?.id === item.id}
                  checked={checked.has(item.id)}
                  cut={clipboard.isCutId(item.id)}
                  dropActive={dropTargetId === item.id}
                  menuOpen={menu?.id === item.id}
                  pinned={pinned?.id === item.id}
                  showCheckbox={!trash}
                  draggable={!trash && itemMovable(item)}
                  contextMenu={!trash}
                  folderNavigable={!trash}
                  acceptsDrop={!trash}
                  handlers={rowHandlers}
                />
              ))}
            </tbody>
          </table>
        </div>
      )}
      {paged.hasMore && (
        <div className={styles.loadMoreRow}>
          <Button type="button" onClick={() => void loadMore()} disabled={paged.loadingMore} data-testid="load-more">
            {paged.loadingMore ? "Loading…" : "Load more"}
          </Button>
          <span className={styles.loadMoreMeta} data-testid="loaded-count">
            {loadedCount} loaded
          </span>
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
            void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] });
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
        open={shareChatItem !== null}
        title={shareChatItem ? `Share “${shareChatItem.name}” to chat` : "Share to chat"}
        testId="share-chat-dialog"
        options={textChannelShareOptions}
        description="Pick a text channel. The file is linked in a new message (no extra caption required)."
        emptyLabel="No text channels in this team"
        busy={shareChatBusy}
        error={shareChatError}
        confirmLabel="Share"
        onClose={() => {
          if (shareChatBusy) return;
          setShareChatItem(null);
          setShareChatError(null);
        }}
        onPick={async (targetChannelName) => {
          if (!shareChatItem || !targetChannelName) return;
          setShareChatBusy(true);
          setShareChatError(null);
          try {
            await messagesApi.post(teamSlug, targetChannelName, {
              body: "",
              fileIds: [shareChatItem.id],
            });
            setShareChatItem(null);
          } catch (e) {
            setShareChatError(e instanceof ApiError ? e.message : "Could not share to chat.");
          } finally {
            setShareChatBusy(false);
          }
        }}
      />

      <PickListDialog
        open={sendOpen}
        title={`Send ${checkedItems.filter((i) => i.kind === ItemKind.File).length || checked.size} file${checked.size === 1 ? "" : "s"} to channel`}
        testId="send-channel-dialog"
        options={sendChannelOptions}
        description="Pick another file channel in this team. Folders stay put — only files are sent."
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
                    data-testid={`cut-${menuItem.name}`}
                    onClick={() => {
                      closeMenu();
                      clipboard.cutOrCopy("cut", [menuItem.id]);
                    }}
                  >
                    Cut
                  </button>
                </li>
                <li>
                  <button
                    type="button"
                    role="menuitem"
                    data-testid={`copy-${menuItem.name}`}
                    onClick={() => {
                      closeMenu();
                      clipboard.cutOrCopy("copy", [menuItem.id]);
                    }}
                  >
                    Copy
                  </button>
                </li>
                {clipboard.clip && (
                  <li>
                    <button
                      type="button"
                      role="menuitem"
                      data-testid="menu-paste"
                      onClick={() => {
                        closeMenu();
                        void clipboard.paste();
                      }}
                    >
                      Paste
                    </button>
                  </li>
                )}
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
                    <li>
                      <button
                        type="button"
                        role="menuitem"
                        data-testid={`share-chat-${menuItem.name}`}
                        onClick={() => {
                          closeMenu();
                          setShareChatError(null);
                          setShareChatItem(menuItem);
                        }}
                      >
                        Share to chat…
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
