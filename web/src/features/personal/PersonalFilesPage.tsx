import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { ItemKind, personalApi, teamsApi, type FileItem } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { formatBytes } from "../../lib/formatBytes";
import { useUploads } from "../../uploads/UploadContext";
import { Button } from "../../ui/Button";
import { UploadButton } from "../files/UploadButton";
import styles from "../files/FileBrowser.module.css";

type Mode = "files" | "recent" | "starred" | "trash";

export function PersonalFilesPage({ mode = "files" }: { mode?: Mode }) {
  const qc = useQueryClient();
  const uploads = useUploads();
  const [selected, setSelected] = useState<FileItem | null>(null);
  const trash = mode === "trash";

  const list = useQuery({
    queryKey: ["personal", mode],
    queryFn: async () => {
      if (mode === "recent") return personalApi.recent();
      if (mode === "starred") return personalApi.starred();
      return personalApi.list({ trash });
    },
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

  const copy = useMutation({
    mutationFn: (item: FileItem) => {
      const team = teams.data?.[0];
      if (!team) throw new Error("Create a team first");
      return personalApi.copyToChannel(item.id, team.slug, "general", item.name);
    },
  });

  const sidebar = (
    <nav className={styles.channelNav} aria-label="My files">
      <a href="/me/files" data-testid="me-files">
        All files
      </a>
      <a href="/me/files/recent">Recent</a>
      <a href="/me/files/starred">Starred</a>
      <a href="/me/files/trash" data-testid="me-trash">
        Trash
      </a>
    </nav>
  );

  return (
    <AppShell title="My files" sidebar={sidebar} details={selected ? <p>{selected.name}</p> : null}>
      <div className={styles.toolbar}>
        {!trash && mode === "files" && (
          <UploadButton
            testId="personal-file-input"
            label="Upload files"
            onFiles={(files) => {
              uploads.enqueuePersonal(files);
              void qc.invalidateQueries({ queryKey: ["personal"] });
            }}
          />
        )}
      </div>

      {list.isLoading && <p>Loading…</p>}
      {!list.isLoading && (list.data?.items.length ?? 0) === 0 && <p>No personal files.</p>}

      <table className={styles.table} data-testid="personal-table">
        <thead>
          <tr>
            <th scope="col">Name</th>
            <th scope="col">Size</th>
            <th scope="col">Actions</th>
          </tr>
        </thead>
        <tbody>
          {(list.data?.items ?? []).map((item) => (
            <tr key={item.id} data-testid={`personal-row-${item.name}`} onClick={() => setSelected(item)}>
              <td>
                {item.kind === ItemKind.Folder ? "📁 " : ""}
                {item.name}
              </td>
              <td>{item.kind === ItemKind.Folder ? "—" : formatBytes(item.sizeBytes)}</td>
              <td>
                {!trash && (
                  <>
                    <Button
                      type="button"
                      data-testid={`copy-${item.name}`}
                      onClick={(e) => {
                        e.stopPropagation();
                        void copy.mutateAsync(item);
                      }}
                    >
                      Copy to channel
                    </Button>{" "}
                    <Button
                      type="button"
                      onClick={(e) => {
                        e.stopPropagation();
                        void personalApi.trash(item.id).then(() => qc.invalidateQueries({ queryKey: ["personal"] }));
                      }}
                    >
                      Move to trash
                    </Button>
                  </>
                )}
                {trash && (
                  <Button
                    type="button"
                    onClick={(e) => {
                      e.stopPropagation();
                      void personalApi.restore(item.id).then(() => qc.invalidateQueries({ queryKey: ["personal"] }));
                    }}
                  >
                    Restore
                  </Button>
                )}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {copy.isSuccess && <p data-testid="copy-success">Copied to team channel.</p>}
    </AppShell>
  );
}
