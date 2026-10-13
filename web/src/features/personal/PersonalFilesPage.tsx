import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { ChannelKind, ItemKind, personalApi, teamsApi, type FileItem } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { formatBytes } from "../../lib/formatBytes";
import { ModifiedAt } from "../../lib/formatModified";
import { DropOverlay } from "../../uploads/DropOverlay";
import { useFileDrop } from "../../uploads/useFileDrop";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { BulkBar } from "../files/BulkBar";
import { CreateFolderDialog } from "../files/CreateFolderDialog";
import { PickListDialog } from "../files/PickListDialog";
import { UploadButton } from "../files/UploadButton";
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
  const [selected, setSelected] = useState<FileItem | null>(null);
  const [folderId, setFolderId] = useState<string | undefined>();
  const [folderStack, setFolderStack] = useState<{ id: string; name: string }[]>([]);
  const [createFolderOpen, setCreateFolderOpen] = useState(false);
  const [createFolderError, setCreateFolderError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [nameQuery, setNameQuery] = useState("");
  const [typeFilter, setTypeFilter] = useState<"all" | "files" | "folders">("all");
  const [sort, setSort] = useState<"name" | "updated" | "size">("name");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [menu, setMenu] = useState<MenuState | null>(null);
  const [copySuccess, setCopySuccess] = useState(false);
  const [checked, setChecked] = useState<Set<string>>(() => new Set());
  const [moveOpen, setMoveOpen] = useState(false);
  const [sendOpen, setSendOpen] = useState(false);
  const [bulkBusy, setBulkBusy] = useState(false);
  const [bulkError, setBulkError] = useState<string | null>(null);
  const menuRef = useRef<HTMLUListElement | null>(null);

  const trash = mode === "trash";
  const canBrowseFolders = mode === "files";
  const canUpload = !trash && mode === "files";
  const canBulk = !trash && mode === "files";

  useEffect(() => {
    if (!canBrowseFolders) {
      setFolderId(undefined);
      setFolderStack([]);
    }
  }, [canBrowseFolders]);

  useEffect(() => {
    setChecked(new Set());
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
  });

  function enqueue(files: File[]) {
    uploads.enqueuePersonal(files, folderId);
    void qc.invalidateQueries({ queryKey: ["personal"] });
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
      void qc.invalidateQueries({ queryKey: ["personal"] });
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

  const visibleItems = useMemo(() => {
    let items = list.data?.items ?? [];
    if (typeFilter === "files") {
      items = items.filter((i) => i.kind === ItemKind.File);
    } else if (typeFilter === "folders") {
      items = items.filter((i) => i.kind === ItemKind.Folder);
    }
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
      return a.name.localeCompare(b.name) * dir;
    });
  }, [list.data, typeFilter, nameQuery, sort, sortDir]);

  const checkedItems = useMemo(
    () => visibleItems.filter((i) => checked.has(i.id)),
    [visibleItems, checked],
  );
  const allVisibleChecked =
    visibleItems.length > 0 && visibleItems.every((i) => checked.has(i.id));
  const someVisibleChecked = checkedItems.length > 0 && !allVisibleChecked;

  const moveFolderOptions = useMemo(
    () =>
      foldersHere
        .filter((f) => !checked.has(f.id))
        .map((f) => ({ id: f.id, label: f.name })),
    [foldersHere, checked],
  );

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

  async function moveIds(ids: string[], targetFolderId: string | null) {
    for (const id of ids) {
      await personalApi.move(id, targetFolderId);
    }
  }

  function openFolder(folder: FileItem) {
    setFolderStack((s) => [...s, { id: folder.id, name: folder.name }]);
    setFolderId(folder.id);
    setSelected(null);
  }

  function goUp() {
    setFolderStack((s) => {
      const next = s.slice(0, -1);
      setFolderId(next.at(-1)?.id);
      return next;
    });
    setSelected(null);
  }

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

  async function onMove(item: FileItem) {
    const choices = foldersHere.filter((f) => f.id !== item.id);
    if (choices.length === 0) {
      await personalApi.move(item.id, null);
      void qc.invalidateQueries({ queryKey: ["personal"] });
      return;
    }
    const target = window.prompt(
      `Move to folder id (blank=root):\n${choices.map((f) => `${f.name}=${f.id}`).join("\n")}`,
    );
    await personalApi.move(item.id, target ? target.trim() : null);
    void qc.invalidateQueries({ queryKey: ["personal"] });
  }

  const menuItem = menu ? visibleItems.find((i) => i.id === menu.id) ?? null : null;
  const currentFolderName = folderStack.at(-1)?.name;
  const heading = mode === "files" && currentFolderName ? currentFolderName : titles[mode];
  const rawCount = list.data?.items.length ?? 0;

  const details = selected ? (
    <div data-testid="details-panel">
      <h2 style={{ margin: 0 }}>{selected.name}</h2>
      <p className={styles.lede}>Kind: {selected.kind === ItemKind.Folder ? "Folder" : "File"}</p>
      {selected.sizeBytes != null && selected.kind !== ItemKind.Folder && (
        <p className={styles.lede}>Size: {formatBytes(selected.sizeBytes)}</p>
      )}
      <p className={styles.lede}>Updated: {new Date(selected.updatedAt).toLocaleString()}</p>
    </div>
  ) : (
    <p className={styles.lede}>Select an item for details.</p>
  );

  return (
    <AppShell title={trash ? "Trash · My files" : "My files"} sidebar={<PersonalSidebar />} details={details}>
      <DropOverlay active={dropActive} label="Drop files to upload to My files" />

      <div className={styles.headingBar} data-testid="dropzone">
        <div>
          <div className={styles.headingRow}>
            <h2 className={styles.heading}>{heading}</h2>
            {!trash && <span className={styles.pill}>Personal</span>}
          </div>
          <p className={styles.lede}>
            {trash ? "Items in your personal trash" : "Your private files"}
          </p>
        </div>
        <div className={styles.toolbarActions}>
          {canBrowseFolders && folderId && (
            <Button type="button" data-testid="personal-up" onClick={goUp}>
              Up
            </Button>
          )}
          {canUpload && (
            <>
              <Button
                type="button"
                data-testid="personal-new-folder"
                onClick={() => {
                  setCreateFolderError(null);
                  setCreateFolderOpen(true);
                }}
              >
                New folder
              </Button>
              <UploadButton testId="personal-file-input" onFiles={enqueue} />
            </>
          )}
        </div>
      </div>

      {canBulk && (
        <BulkBar
          count={checked.size}
          busy={bulkBusy || creatingFolder}
          onClear={() => setChecked(new Set())}
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

      <div className={styles.controls}>
        <label className={styles.search}>
          <span className={styles.searchIcon} aria-hidden="true">
            <svg viewBox="0 0 24 24" fill="none">
              <circle cx="11" cy="11" r="6.5" stroke="currentColor" strokeWidth="1.8" />
              <path d="M16.2 16.2 20 20" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
            </svg>
          </span>
          <span className="visually-hidden">Filter by name</span>
          <input
            className={styles.searchInput}
            data-testid="filter-name"
            type="search"
            placeholder="Filter by name"
            value={nameQuery}
            onChange={(e) => setNameQuery(e.target.value)}
          />
        </label>

        <div className={styles.controlsMeta}>
          <div className={styles.controlBlock}>
            <span className={styles.controlLabel} id="personal-type-filter-label">
              Type
            </span>
            <div className={styles.segment} role="group" aria-labelledby="personal-type-filter-label">
              {(
                [
                  { id: "all", label: "All" },
                  { id: "files", label: "Files" },
                  { id: "folders", label: "Folders" },
                ] as const
              ).map((opt) => (
                <button
                  key={opt.id}
                  type="button"
                  className={styles.segmentBtn}
                  data-active={typeFilter === opt.id ? "true" : "false"}
                  aria-pressed={typeFilter === opt.id}
                  onClick={() => setTypeFilter(opt.id)}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>

          <div className={styles.controlBlock}>
            <span className={styles.controlLabel} id="personal-sort-label">
              Sort by
            </span>
            <div className={styles.sortControls}>
              <div className={styles.segment} role="group" aria-labelledby="personal-sort-label">
                {(
                  [
                    { id: "name", label: "Name" },
                    { id: "updated", label: "Updated" },
                    { id: "size", label: "Size" },
                  ] as const
                ).map((opt) => (
                  <button
                    key={opt.id}
                    type="button"
                    className={styles.segmentBtn}
                    data-active={sort === opt.id ? "true" : "false"}
                    aria-pressed={sort === opt.id}
                    onClick={() => {
                      if (sort === opt.id) {
                        setSortDir((d) => (d === "asc" ? "desc" : "asc"));
                      } else {
                        setSort(opt.id);
                        setSortDir(opt.id === "name" ? "asc" : "desc");
                      }
                    }}
                  >
                    {opt.label}
                  </button>
                ))}
              </div>
              <button
                type="button"
                className={styles.sortDir}
                data-dir={sortDir}
                aria-label={sortDir === "asc" ? "Ascending" : "Descending"}
                title={sortDir === "asc" ? "Ascending" : "Descending"}
                onClick={() => setSortDir((d) => (d === "asc" ? "desc" : "asc"))}
              >
                <span aria-hidden="true">{sortDir === "asc" ? "↑" : "↓"}</span>
                <span className={styles.sortDirText}>{sortDir === "asc" ? "Asc" : "Desc"}</span>
              </button>
            </div>
          </div>
        </div>
      </div>

      {error && (
        <p className={styles.error} role="alert">
          {error}
        </p>
      )}

      {list.isLoading && <p>Loading…</p>}
      {!list.isLoading && rawCount === 0 && (
        <p data-testid="personal-empty">{canUpload ? "No files yet" : "No items here."}</p>
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
                <th scope="col">Name</th>
                <th scope="col">Size</th>
                <th scope="col">Modified</th>
                <th scope="col">
                  <span className="visually-hidden">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {visibleItems.map((item) => (
                <tr
                  key={item.id}
                  data-testid={`personal-row-${item.name}`}
                  data-selected={selected?.id === item.id || checked.has(item.id) ? "true" : "false"}
                  onClick={() => setSelected(item)}
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
                      />
                    )}
                  </td>
                  <td>
                    {item.kind === ItemKind.Folder && canBrowseFolders && !trash ? (
                      <button
                        type="button"
                        className={styles.linkish}
                        data-testid={`personal-open-folder-${item.name}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          openFolder(item);
                        }}
                      >
                        {item.name}
                      </button>
                    ) : (
                      <span className={styles.linkish}>
                        {item.kind === ItemKind.Folder ? `${item.name}/` : item.name}
                      </span>
                    )}
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

      {canUpload && (
        <div className={styles.dropHint}>Drop files anywhere on this page to upload to My files</div>
      )}

      {copySuccess && <p data-testid="copy-success">Copied to team channel.</p>}

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
              await moveIds([...checked], folder.id);
              setChecked(new Set());
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
        open={moveOpen}
        title={`Move ${checked.size} item${checked.size === 1 ? "" : "s"}`}
        testId="move-to-dialog"
        options={moveFolderOptions}
        allowNone
        noneLabel={folderId ? "Parent folder / root" : "My files root"}
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
            void qc.invalidateQueries({ queryKey: ["personal"] });
          } catch (e) {
            setBulkError(e instanceof Error ? e.message : "Move failed");
          } finally {
            setBulkBusy(false);
          }
        }}
      />

      <PickListDialog
        open={sendOpen}
        title={`Send files to channel`}
        testId="send-channel-dialog"
        options={sendChannelOptions}
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
                        onClick={() => {
                          closeMenu();
                          void onMove(menuItem);
                        }}
                      >
                        Move
                      </button>
                    </li>
                  </>
                )}
                {menuItem.kind === ItemKind.File && (
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
                )}
                <li>
                  <button
                    type="button"
                    role="menuitem"
                    data-testid={`trash-${menuItem.name}`}
                    onClick={() => {
                      closeMenu();
                      void personalApi.trash(menuItem.id).then(() => qc.invalidateQueries({ queryKey: ["personal"] }));
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
                    void personalApi.restore(menuItem.id).then(() => qc.invalidateQueries({ queryKey: ["personal"] }));
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
