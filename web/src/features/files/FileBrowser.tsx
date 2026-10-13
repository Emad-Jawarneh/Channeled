import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChannelKind, filesApi, ItemKind, teamsApi, type FileItem } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { describeFileKind, formatFileType } from "../../lib/describeFileKind";
import { formatBytes } from "../../lib/formatBytes";
import { ModifiedAt } from "../../lib/formatModified";
import { useChannelFilesChanged } from "../../realtime/useChannelHub";
import { DropOverlay } from "../../uploads/DropOverlay";
import { useFileDrop } from "../../uploads/useFileDrop";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { ChannelSidebar } from "../teams/ChannelSidebar";
import { BulkBar } from "./BulkBar";
import { CreateFolderDialog } from "./CreateFolderDialog";
import { PickListDialog } from "./PickListDialog";
import { FilesSearchField } from "./FilesSearchField";
import { SelectionControls } from "./SelectionControls";
import { ShareLinkDialog } from "./ShareLinkDialog";
import { nextSortState, SortableTh, type SortColumn } from "./SortableTh";
import { UploadButton } from "./UploadButton";
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
  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });
  const channelId = (channels.data ?? []).find((c) => c.name === channelName)?.id;
  useChannelFilesChanged(channelId, teamSlug, channelName);
  const [selected, setSelected] = useState<FileItem | null>(null);
  const [folderId, setFolderId] = useState<string | undefined>();
  const [sort, setSort] = useState<SortColumn>("name");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [nameQuery, setNameQuery] = useState("");
  const [menu, setMenu] = useState<MenuState | null>(null);
  const menuRef = useRef<HTMLUListElement | null>(null);

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
  const [error, setError] = useState<string | null>(null);
  const [createFolderOpen, setCreateFolderOpen] = useState(false);
  const [createFolderError, setCreateFolderError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [shareItem, setShareItem] = useState<FileItem | null>(null);
  const [checked, setChecked] = useState<Set<string>>(() => new Set());
  const [moveOpen, setMoveOpen] = useState(false);
  const [sendOpen, setSendOpen] = useState(false);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkError, setBulkError] = useState<string | null>(null);

  const listKey = ["items", teamSlug, channelName, folderId, trash];
  const list = useQuery({
    queryKey: listKey,
    queryFn: () => filesApi.list(teamSlug, channelName, { folderId, trash }),
  });

  useEffect(() => {
    setChecked(new Set());
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

  const move = useMutation({
    mutationFn: ({ id, targetFolderId }: { id: string; targetFolderId: string | null }) =>
      filesApi.move(teamSlug, channelName, id, targetFolderId),
    onSuccess: () => void qc.invalidateQueries({ queryKey: ["items", teamSlug, channelName] }),
  });

  const folders = useMemo(
    () => (list.data?.items ?? []).filter((i) => i.kind === ItemKind.Folder && !i.deletedAt),
    [list.data],
  );

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

  const moveFolderOptions = useMemo(
    () =>
      folders
        .filter((f) => !checked.has(f.id))
        .map((f) => ({ id: f.id, label: f.name })),
    [folders, checked],
  );

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

  async function moveIds(ids: string[], targetFolderId: string | null, targetChannelName?: string | null) {
    for (const id of ids) {
      await filesApi.move(teamSlug, channelName, id, targetFolderId, targetChannelName);
    }
  }

  const details = selected ? (
    <div data-testid="details-panel">
      <h2 style={{ margin: 0 }}>{selected.name}</h2>
      <p className={styles.lede}>Kind: {describeFileKind(selected)}</p>
      {selected.sizeBytes != null && <p className={styles.lede}>Size: {formatBytes(selected.sizeBytes)}</p>}
      <p className={styles.lede}>Updated: {new Date(selected.updatedAt).toLocaleString()}</p>
    </div>
  ) : (
    <p className={styles.lede}>Select an item for details.</p>
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

  async function onMove(item: FileItem) {
    const choices = folders.filter((f) => f.id !== item.id);
    if (choices.length === 0) {
      await move.mutateAsync({ id: item.id, targetFolderId: null });
      return;
    }
    const target = window.prompt(
      `Move to folder id (blank=root):\n${choices.map((f) => `${f.name}=${f.id}`).join("\n")}`,
    );
    await move.mutateAsync({ id: item.id, targetFolderId: target ? target.trim() : null });
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

  return (
    <AppShell
      title={trash ? `Trash · #${channelName}` : `#${channelName}`}
      sidebar={<ChannelSidebar teamSlug={teamSlug} channelName={channelName} trash={trash} />}
      details={details}
      topbarExtra={
        <FilesSearchField value={nameQuery} onChange={setNameQuery} placeholder="Filter files…" />
      }
    >
      <DropOverlay active={dropActive} label={`Drop files to upload to #${channelName}`} />
      <div className={styles.headingBar} data-testid="dropzone">
        <div>
          <div className={styles.headingRow}>
            <h2 className={styles.heading}>{trash ? "Trash" : channelName}</h2>
            {!trash && <span className={styles.pill}>File channel</span>}
          </div>
          <p className={styles.lede}>{trash ? `Items removed from #${channelName}` : `Files in #${channelName}`}</p>
        </div>
        <div className={styles.toolbarActions}>
          {folderId && (
            <Button type="button" onClick={() => setFolderId(undefined)}>
              Up
            </Button>
          )}
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

      {!trash && (
        <BulkBar
          count={checked.size}
          busy={bulkBusy || creatingFolder}
          onClear={selectNone}
          onNewFolder={() => {
            setCreateFolderError(null);
            setCreateFolderOpen(true);
          }}
          onMoveTo={() => {
            setBulkError(null);
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
      {list.isError && <p className={styles.error}>Failed to load items.</p>}
      {!list.isLoading && (list.data?.items.length ?? 0) === 0 && (
        <p data-testid="empty-state">No files in {channelName} yet</p>
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
              {visibleItems.map((item) => (
                <tr
                  key={item.id}
                  data-testid={`row-${item.name}`}
                  data-selected={selected?.id === item.id || checked.has(item.id) ? "true" : "false"}
                  onClick={() => setSelected(item)}
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
                      />
                    )}
                  </td>
                  <td>
                    {item.kind === ItemKind.Folder ? (
                      <button
                        type="button"
                        className={styles.linkish}
                        data-testid={`open-folder-${item.name}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setFolderId(item.id);
                        }}
                      >
                        {item.name}
                      </button>
                    ) : (
                      <span className={styles.linkish}>{item.name}</span>
                    )}
                  </td>
                  <td>
                    <span className={styles.typeCell}>{formatFileType(item)}</span>
                  </td>
                  <td>{item.kind === ItemKind.Folder ? "—" : formatBytes(item.sizeBytes)}</td>
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
              ))}
            </tbody>
          </table>
        </div>
      )}
      {!trash && (
        <div className={styles.dropHint}>Drop files anywhere on this page to upload to #{channelName}</div>
      )}

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
            if (checked.size > 0) {
              await moveIds([...checked], folder.id);
              setChecked(new Set());
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
        open={moveOpen}
        title={`Move ${checked.size} item${checked.size === 1 ? "" : "s"}`}
        testId="move-to-dialog"
        options={moveFolderOptions}
        allowNone
        noneLabel={folderId ? "Parent folder / root" : "Channel root"}
        busy={bulkBusy}
        error={bulkError}
        confirmLabel="Move"
        onClose={() => {
          if (bulkBusy) return;
          setMoveOpen(false);
          setBulkError(null);
        }}
        onPick={async (targetFolderId) => {
          setBulkBusy(true);
          setBulkError(null);
          try {
            await moveIds([...checked], targetFolderId);
            setChecked(new Set());
            setMoveOpen(false);
            void qc.invalidateQueries({ queryKey: listKey });
          } catch (e) {
            setBulkError(e instanceof Error ? e.message : "Move failed");
          } finally {
            setBulkBusy(false);
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
          const fileIds = checkedItems.filter((i) => i.kind === ItemKind.File).map((i) => i.id);
          if (fileIds.length === 0) {
            setBulkError("Only files can be sent to another channel.");
            return;
          }
          setBulkBusy(true);
          setBulkError(null);
          try {
            await moveIds(fileIds, null, targetChannelName);
            setChecked(new Set());
            setSendOpen(false);
            void qc.invalidateQueries({ queryKey: listKey });
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
                    onClick={() => {
                      closeMenu();
                      void onMove(menuItem);
                    }}
                  >
                    Move
                  </button>
                </li>
                {menuItem.kind === ItemKind.File && (
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
