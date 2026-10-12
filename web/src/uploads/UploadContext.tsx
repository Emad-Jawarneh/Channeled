import {
  createContext,
  useCallback,
  useContext,
  useMemo,
  useRef,
  useState,
  type ReactNode,
} from "react";
import { filesApi, personalApi } from "../api/channeled";
import { ensureCsrf } from "../api/http";

export type UploadJob = {
  id: string;
  fileName: string;
  sizeBytes: number;
  progress: number;
  status: "queued" | "uploading" | "paused" | "done" | "error";
  error?: string;
  scope: "channel" | "personal";
  teamSlug?: string;
  channelName?: string;
  folderId?: string | null;
  file: File;
};

type UploadApi = {
  jobs: UploadJob[];
  enqueueChannel: (files: File[], teamSlug: string, channelName: string, folderId?: string | null) => void;
  enqueuePersonal: (files: File[], folderId?: string | null) => void;
  pause: (id: string) => void;
  resume: (id: string) => void;
  retry: (id: string) => void;
};

const UploadContext = createContext<UploadApi | null>(null);
const FILE_CONCURRENCY = 3;
const PART_CONCURRENCY = 3;

async function sha256Hex(file: File): Promise<string> {
  const buffer = await file.arrayBuffer();
  const hash = await crypto.subtle.digest("SHA-256", buffer);
  return [...new Uint8Array(hash)].map((b) => b.toString(16).padStart(2, "0")).join("");
}

export function UploadProvider({ children }: { children: ReactNode }) {
  const [jobs, setJobs] = useState<UploadJob[]>([]);
  const jobsRef = useRef(jobs);
  jobsRef.current = jobs;
  const paused = useRef(new Set<string>());
  const running = useRef(0);
  const queue = useRef<string[]>([]);

  const update = useCallback((id: string, patch: Partial<UploadJob>) => {
    setJobs((prev) => {
      const next = prev.map((j) => (j.id === id ? { ...j, ...patch } : j));
      jobsRef.current = next;
      return next;
    });
  }, []);

  const processJob = useCallback(
    async (job: UploadJob) => {
      try {
        update(job.id, { status: "uploading", progress: 0, error: undefined });
        const csrf = await ensureCsrf();
        const sha = await sha256Hex(job.file);

        let uploadId: string;
        let partSize: number;
        if (job.scope === "channel") {
          const start = await filesApi.startUpload({
            teamSlug: job.teamSlug!,
            channelName: job.channelName!,
            fileName: job.fileName,
            declaredSizeBytes: job.sizeBytes,
            contentType: job.file.type || "application/octet-stream",
            folderId: job.folderId,
          });
          uploadId = start.uploadId;
          partSize = start.partSizeBytes;
        } else {
          const start = await personalApi.startUpload({
            fileName: job.fileName,
            declaredSizeBytes: job.sizeBytes,
            contentType: job.file.type || "application/octet-stream",
            folderId: job.folderId,
          });
          uploadId = start.uploadId;
          partSize = start.partSizeBytes;
        }

        const partCount = Math.max(1, Math.ceil(job.sizeBytes / partSize));
        let nextPart = 1;
        const workers: Promise<void>[] = [];

        const runPart = async () => {
          while (nextPart <= partCount) {
            if (paused.current.has(job.id)) {
              update(job.id, { status: "paused" });
              return;
            }
            const partNumber = nextPart++;
            const start = (partNumber - 1) * partSize;
            const end = Math.min(start + partSize, job.sizeBytes);
            const blob = job.file.slice(start, end);
            await filesApi.putPart(uploadId, partNumber, blob, csrf);
            update(job.id, { progress: Math.round((partNumber / partCount) * 100) });
          }
        };

        for (let i = 0; i < Math.min(PART_CONCURRENCY, partCount); i++) {
          workers.push(runPart());
        }
        await Promise.all(workers);

        if (paused.current.has(job.id)) {
          return;
        }

        await filesApi.complete(uploadId, sha);
        update(job.id, { status: "done", progress: 100 });
      } catch (err) {
        update(job.id, {
          status: "error",
          error: err instanceof Error ? err.message : "Upload failed",
        });
      }
    },
    [update],
  );

  const pump = useCallback(() => {
    while (running.current < FILE_CONCURRENCY && queue.current.length > 0) {
      const id = queue.current.shift()!;
      const job = jobsRef.current.find((j) => j.id === id);
      if (!job || job.status === "done") {
        continue;
      }
      running.current += 1;
      void processJob(job).finally(() => {
        running.current -= 1;
        pump();
      });
    }
  }, [processJob]);

  const enqueue = useCallback(
    (next: Omit<UploadJob, "id" | "progress" | "status">[]) => {
      const created: UploadJob[] = next.map((j) => ({
        ...j,
        id: crypto.randomUUID(),
        progress: 0,
        status: "queued" as const,
      }));
      setJobs((prev) => {
        const merged = [...created, ...prev];
        jobsRef.current = merged;
        return merged;
      });
      queue.current.push(...created.map((c) => c.id));
      queueMicrotask(() => pump());
    },
    [pump],
  );

  const api = useMemo<UploadApi>(
    () => ({
      jobs,
      enqueueChannel: (files, teamSlug, channelName, folderId) =>
        enqueue(
          files.map((file) => ({
            fileName: file.name,
            sizeBytes: file.size,
            scope: "channel" as const,
            teamSlug,
            channelName,
            folderId,
            file,
          })),
        ),
      enqueuePersonal: (files, folderId) =>
        enqueue(
          files.map((file) => ({
            fileName: file.name,
            sizeBytes: file.size,
            scope: "personal" as const,
            folderId,
            file,
          })),
        ),
      pause: (id) => {
        paused.current.add(id);
        update(id, { status: "paused" });
      },
      resume: (id) => {
        paused.current.delete(id);
        queue.current.push(id);
        update(id, { status: "queued" });
        pump();
      },
      retry: (id) => {
        paused.current.delete(id);
        queue.current.push(id);
        update(id, { status: "queued", error: undefined, progress: 0 });
        pump();
      },
    }),
    [enqueue, jobs, pump, update],
  );

  return <UploadContext.Provider value={api}>{children}</UploadContext.Provider>;
}

export function useUploads(): UploadApi {
  const ctx = useContext(UploadContext);
  if (!ctx) {
    throw new Error("useUploads requires UploadProvider");
  }
  return ctx;
}
