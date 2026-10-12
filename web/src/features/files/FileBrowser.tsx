import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { filesApi, ItemKind, teamsApi, type FileItem } from "../../api/channeled";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { AppShell } from "../../components/AppShell";
import { CreateFolderDialog } from "./CreateFolderDialog";
import styles from "./FileBrowser.module.css";

type Props = {
  teamSlug: string;
  channelName: string;
  trash?: boolean;
};

export function FileBrowser({ teamSlug, channelName, trash = false }: Props) {
  const qc = useQueryClient();
  const uploads = useUploads();
  const [selected, setSelected] = useState<FileItem | null>(null);
  const [folderId, setFolderId] = useState<string | undefined>();
  const [sort, setSort] = useState("name");
  const [menuId, setMenuId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [createFolderOpen, setCreateFolderOpen] = useState(false);
  const [createFolderError, setCreateFolderError] = useState<string | null>(null);
  const [creatingFolder, setCreatingFolder] = useState(false);

  const listKey = ["items", teamSlug, channelName, folderId, trash, sort];
  const list = useQuery({
    queryKey: listKey,
    queryFn: () => filesApi.list(teamSlug, channelName, { folderId, trash, sort }),
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

  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
  });

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

  const sidebar = (
    <div>
      <p className={styles.sideTitle}>{teamSlug}</p>
      <nav className={styles.channelNav} aria-label="Channels">
        {(channels.data ?? []).map((ch) => (
          <a
            key={ch.id}
            href={`/t/${teamSlug}/c/${ch.name}${trash ? "/trash" : ""}`}
            className={ch.name === channelName ? styles.active : undefined}
            data-testid={`channel-${ch.name}`}
          >
            # {ch.name}
          </a>
        ))}
      </nav>
      <a href={`/t/${teamSlug}/c/${channelName}/trash`} data-testid="channel-trash-link">
        Trash
      </a>
      <a href={`/t/${teamSlug}/settings/members`}>Team settings</a>
    </div>
  );

  const details = selected ? (
    <div data-testid="details-panel">
      <h2>{selected.name}</h2>
      <p>Kind: {selected.kind === ItemKind.Folder ? "Folder" : "File"}</p>
      {selected.sizeBytes != null && <p>Size: {selected.sizeBytes} bytes</p>}
      <p>Updated: {new Date(selected.updatedAt).toLocaleString()}</p>
    </div>
  ) : (
    <p>Select an item for details.</p>
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
      sidebar={sidebar}
      details={details}
    >
      <div className={styles.toolbar}>
        {!trash && (
          <>
            <label className={styles.upload}>
              Upload
              <input
                data-testid="file-input"
                type="file"
                multiple
                onChange={(e) => {
                  const files = [...(e.target.files ?? [])];
                  if (files.length) {
                    uploads.enqueueChannel(files, teamSlug, channelName, folderId);
                    void qc.invalidateQueries({ queryKey: listKey });
                  }
                  e.target.value = "";
                }}
              />
            </label>
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
          </>
        )}
        <label>
          Sort
          <select
            data-testid="sort-select"
            value={sort}
            onChange={(e) => setSort(e.target.value)}
          >
            <option value="name">Name</option>
            <option value="updated">Updated</option>
            <option value="size">Size</option>
          </select>
        </label>
        {folderId && (
          <Button type="button" onClick={() => setFolderId(undefined)}>
            Up
          </Button>
        )}
      </div>

      {error && <p className={styles.error}>{error}</p>}
      {list.isLoading && <p>Loading…</p>}
      {list.isError && <p className={styles.error}>Failed to load items.</p>}
      {!list.isLoading && (list.data?.items.length ?? 0) === 0 && (
        <p data-testid="empty-state">No items yet.</p>
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
        <table className={styles.table} data-testid="file-table">
          <thead>
            <tr>
              <th scope="col">Name</th>
              <th scope="col">Size</th>
              <th scope="col">Updated</th>
              <th scope="col">
                <span className="visually-hidden">Actions</span>
              </th>
            </tr>
          </thead>
          <tbody>
            {(list.data?.items ?? []).map((item) => (
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
                      📁 {item.name}
                    </button>
                  ) : (
                    item.name
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
                      Actions
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
