import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { filesApi, ItemKind, teamsApi, type FileItem } from "../../api/channeled";
import { useChannelFilesChanged } from "../../realtime/useChannelHub";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { AppShell } from "../../components/AppShell";
import { ChannelSidebar } from "../teams/ChannelSidebar";
import { CreateFolderDialog } from "./CreateFolderDialog";
import { UploadButton } from "./UploadButton";
import { ShareLinkDialog } from "./ShareLinkDialog";
import styles from "./FileBrowser.module.css";

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
  const [sort, setSort] = useState<"name" | "updated" | "size">("name");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [typeFilter, setTypeFilter] = useState<"all" | "files" | "folders">("all");
  const [nameQuery, setNameQuery] = useState("");
  const [menuId, setMenuId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [createFolderOpen, setCreateFolderOpen] = useState(false);
  const [createFolderError, setCreateFolderError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);
  const [shareItem, setShareItem] = useState<FileItem | null>(null);

  const sortParam = `${sort}_${sortDir}`;
  const listKey = ["items", teamSlug, channelName, folderId, trash, sortParam];
  const list = useQuery({
    queryKey: listKey,
    queryFn: () => filesApi.list(teamSlug, channelName, { folderId, trash, sort: sortParam }),
  });

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
    if (typeFilter === "files") {
      items = items.filter((i) => i.kind === ItemKind.File);
    } else if (typeFilter === "folders") {
      items = items.filter((i) => i.kind === ItemKind.Folder);
    }
    const q = nameQuery.trim().toLowerCase();
    if (q) {
      items = items.filter((i) => i.name.toLowerCase().includes(q));
    }
    return items;
  }, [list.data, typeFilter, nameQuery]);

  const details = selected ? (
    <div data-testid="details-panel">
      <h2 style={{ margin: 0 }}>{selected.name}</h2>
      <p className={styles.lede}>Kind: {selected.kind === ItemKind.Folder ? "Folder" : "File"}</p>
      {selected.sizeBytes != null && <p className={styles.lede}>Size: {selected.sizeBytes} bytes</p>}
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

  return (
    <AppShell
      title={trash ? `Trash · #${channelName}` : `#${channelName}`}
      sidebar={<ChannelSidebar teamSlug={teamSlug} channelName={channelName} trash={trash} />}
      details={details}
    >
      <div className={styles.headingBar}>
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
              <UploadButton
                testId="file-input"
                onFiles={(files) => {
                  uploads.enqueueChannel(files, teamSlug, channelName, folderId);
                  void qc.invalidateQueries({ queryKey: listKey });
                }}
              />
            </>
          )}
        </div>
      </div>

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
            <span className={styles.controlLabel} id="type-filter-label">
              Type
            </span>
            <div className={styles.segment} role="group" aria-labelledby="type-filter-label">
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
                  data-testid={`filter-type-${opt.id}`}
                  aria-pressed={typeFilter === opt.id}
                  onClick={() => setTypeFilter(opt.id)}
                >
                  {opt.label}
                </button>
              ))}
            </div>
          </div>

          <div className={styles.controlBlock}>
            <span className={styles.controlLabel} id="sort-label">
              Sort by
            </span>
            <div className={styles.sortControls}>
              <div
                className={styles.segment}
                role="group"
                aria-labelledby="sort-label"
                data-testid="sort-select"
                data-sort={sortParam}
              >
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
                    data-testid={`sort-${opt.id}`}
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
                data-testid="sort-direction"
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

      {error && <p className={styles.error}>{error}</p>}
      {list.isLoading && <p>Loading…</p>}
      {list.isError && <p className={styles.error}>Failed to load items.</p>}
      {!list.isLoading && (list.data?.items.length ?? 0) === 0 && (
        <p data-testid="empty-state">No files in {channelName} yet</p>
      )}
      {!list.isLoading && (list.data?.items.length ?? 0) > 0 && visibleItems.length === 0 && (
        <p data-testid="filter-empty">No items match these filters</p>
      )}

      <div
        className={styles.dropzone}
        data-testid="dropzone"
        onDragOver={(e) => e.preventDefault()}
        onDrop={(e) => {
          e.preventDefault();
          if (trash) return;
          const files = [...e.dataTransfer.files];
          if (files.length) {
            uploads.enqueueChannel(files, teamSlug, channelName, folderId);
          }
        }}
      >
        <div className={styles.tableWrap}>
          <table className={styles.table} data-testid="file-table">
            <thead>
              <tr>
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
                  data-testid={`row-${item.name}`}
                  data-selected={selected?.id === item.id ? "true" : "false"}
                  onClick={() => setSelected(item)}
                >
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
                  <td>{item.sizeBytes ?? "—"}</td>
                  <td>{new Date(item.updatedAt).toLocaleString()}</td>
                  <td>
                    <div className={styles.actionWrap}>
                      <button
                        type="button"
                        aria-haspopup="menu"
                        aria-expanded={menuId === item.id}
                        data-testid={`actions-${item.name}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          setMenuId(menuId === item.id ? null : item.id);
                        }}
                      >
                        ⋯
                      </button>
                      {menuId === item.id && (
                        <ul className={styles.menu} role="menu">
                          {!trash && (
                            <>
                              <li>
                                <button type="button" role="menuitem" onClick={() => void onRename(item)}>
                                  Rename
                                </button>
                              </li>
                              <li>
                                <button type="button" role="menuitem" onClick={() => void onMove(item)}>
                                  Move
                                </button>
                              </li>
                              {item.kind === ItemKind.File && (
                                <li>
                                  <button
                                    type="button"
                                    role="menuitem"
                                    data-testid={`share-${item.name}`}
                                    onClick={() => {
                                      setMenuId(null);
                                      setShareItem(item);
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
                                  data-testid={`trash-${item.name}`}
                                  onClick={() => void trashMut.mutateAsync(item.id)}
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
                                data-testid={`restore-${item.name}`}
                                onClick={() => void restore.mutateAsync(item.id)}
                              >
                                Restore
                              </button>
                            </li>
                          )}
                        </ul>
                      )}
                    </div>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
        {!trash && <div className={styles.dropHint}>Drop files here to upload to # {channelName}</div>}
      </div>

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
        onClose={() => {
          if (creatingFolder) return;
          setCreateFolderOpen(false);
          setCreateFolderError(null);
        }}
        onCreate={async (name) => {
          setCreatingFolder(true);
          setCreateFolderError(null);
          try {
            await filesApi.createFolder(teamSlug, channelName, name, folderId ?? null);
            setCreateFolderOpen(false);
            void qc.invalidateQueries({ queryKey: listKey });
          } catch (e) {
            setCreateFolderError(e instanceof Error ? e.message : "Could not create folder.");
          } finally {
            setCreatingFolder(false);
          }
        }}
      />
    </AppShell>
  );
}
