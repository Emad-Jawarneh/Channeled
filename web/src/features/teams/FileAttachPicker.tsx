import { useQuery } from "@tanstack/react-query";
import {
  forwardRef,
  useEffect,
  useImperativeHandle,
  useMemo,
  useRef,
  useState,
} from "react";
import { ChannelKind, filesApi, ItemKind, teamsApi } from "../../api/channeled";
import { formatBytes } from "../../lib/formatBytes";
import local from "./TextChannelPage.module.css";

export type FileAttachPick = {
  id: string;
  name: string;
  channelName: string;
};

export type FileAttachPickerHandle = {
  move: (delta: number) => void;
  /** Enter/Tab: drill or pick. Returns true if handled. */
  activate: () => boolean;
  /** Escape/Back: climb or close. Returns true if climbed (not closed). */
  back: () => boolean;
};

type FolderCrumb = { folderId: string; folderName: string };

type Nav =
  | { view: "channels" }
  | { view: "items"; channelName: string; path: FolderCrumb[] };

type Row =
  | { kind: "channel"; id: string; label: string; channelName: string }
  | { kind: "folder"; id: string; label: string; channelName: string; folderId: string }
  | { kind: "file"; id: string; label: string; channelName: string; meta?: string };

type Props = {
  open: boolean;
  teamSlug: string;
  filter: string;
  excludeIds: string[];
  onPick: (file: FileAttachPick) => void;
  onClose: () => void;
};

function filterRows(rows: Row[], query: string): Row[] {
  const q = query.trim().toLowerCase();
  if (!q) return rows;
  return rows.filter((row) => row.label.toLowerCase().includes(q));
}

export const FileAttachPicker = forwardRef<FileAttachPickerHandle, Props>(function FileAttachPicker(
  { open, teamSlug, filter, excludeIds, onPick, onClose },
  ref,
) {
  const [nav, setNav] = useState<Nav>({ view: "channels" });
  const [activeIndex, setActiveIndex] = useState(0);
  const listRef = useRef<HTMLUListElement>(null);
  const rowsRef = useRef<Row[]>([]);
  const navRef = useRef(nav);
  const activeRef = useRef(activeIndex);
  const onPickRef = useRef(onPick);
  const onCloseRef = useRef(onClose);
  navRef.current = nav;
  activeRef.current = activeIndex;
  onPickRef.current = onPick;
  onCloseRef.current = onClose;

  useEffect(() => {
    if (open) {
      setNav({ view: "channels" });
      setActiveIndex(0);
    }
  }, [open]);

  const channels = useQuery({
    queryKey: ["channels", teamSlug],
    queryFn: () => teamsApi.channels(teamSlug),
    enabled: open,
  });

  const fileChannels = useMemo(
    () =>
      (channels.data ?? []).filter(
        (c) => (c.kind ?? ChannelKind.File) === ChannelKind.File && !c.archivedAt,
      ),
    [channels.data],
  );

  const currentFolderId =
    nav.view === "items" && nav.path.length > 0 ? nav.path[nav.path.length - 1]!.folderId : null;

  const itemsQuery = useQuery({
    queryKey: [
      "attach-items",
      teamSlug,
      nav.view === "items" ? nav.channelName : "",
      currentFolderId,
    ],
    queryFn: () =>
      filesApi.list(teamSlug, (nav as Extract<Nav, { view: "items" }>).channelName, {
        folderId: currentFolderId ?? undefined,
      }),
    enabled: open && nav.view === "items",
  });

  const rows = useMemo(() => {
    if (nav.view === "channels") {
      return filterRows(
        fileChannels.map(
          (c): Row => ({
            kind: "channel",
            id: c.id,
            label: c.name,
            channelName: c.name,
          }),
        ),
        filter,
      );
    }

    const exclude = new Set(excludeIds);
    const items = itemsQuery.data?.items ?? [];
    const mapped: Row[] = [];
    for (const item of items) {
      if (item.kind === ItemKind.Folder) {
        mapped.push({
          kind: "folder",
          id: item.id,
          label: item.name,
          channelName: nav.channelName,
          folderId: item.id,
        });
      } else if (item.kind === ItemKind.File && !exclude.has(item.id)) {
        mapped.push({
          kind: "file",
          id: item.id,
          label: item.name,
          channelName: nav.channelName,
          meta: formatBytes(item.sizeBytes),
        });
      }
    }
    mapped.sort((a, b) => {
      const ak = a.kind === "folder" ? 0 : 1;
      const bk = b.kind === "folder" ? 0 : 1;
      if (ak !== bk) return ak - bk;
      return a.label.localeCompare(b.label);
    });
    return filterRows(mapped, filter);
  }, [nav, fileChannels, filter, excludeIds, itemsQuery.data?.items]);

  rowsRef.current = rows;

  useEffect(() => {
    setActiveIndex((i) => (rows.length === 0 ? 0 : Math.min(i, rows.length - 1)));
  }, [rows.length]);

  useEffect(() => {
    const el = listRef.current?.querySelector<HTMLElement>(`[data-attach-index="${activeIndex}"]`);
    el?.scrollIntoView({ block: "nearest" });
  }, [activeIndex, rows]);

  function activateRow(row: Row) {
    if (row.kind === "channel") {
      setNav({ view: "items", channelName: row.channelName, path: [] });
      setActiveIndex(0);
      return;
    }
    if (row.kind === "folder") {
      const current = navRef.current;
      const path = current.view === "items" ? current.path : [];
      setNav({
        view: "items",
        channelName: row.channelName,
        path: [...path, { folderId: row.folderId, folderName: row.label }],
      });
      setActiveIndex(0);
      return;
    }
    onPickRef.current({ id: row.id, name: row.label, channelName: row.channelName });
  }

  function goBack(): boolean {
    const current = navRef.current;
    if (current.view === "items" && current.path.length > 0) {
      setNav({
        view: "items",
        channelName: current.channelName,
        path: current.path.slice(0, -1),
      });
      setActiveIndex(0);
      return true;
    }
    if (current.view === "items") {
      setNav({ view: "channels" });
      setActiveIndex(0);
      return true;
    }
    return false;
  }

  useImperativeHandle(ref, () => ({
    move(delta) {
      const n = rowsRef.current.length;
      if (n === 0) return;
      setActiveIndex((i) => (i + delta + n * 10) % n);
    },
    activate() {
      const row = rowsRef.current[activeRef.current] ?? rowsRef.current[0];
      if (!row) return false;
      activateRow(row);
      return true;
    },
    back() {
      return goBack();
    },
  }));

  if (!open) return null;

  const title =
    nav.view === "channels"
      ? "File channels"
      : nav.path.length > 0
        ? `${nav.channelName} / ${nav.path.map((p) => p.folderName).join(" / ")}`
        : `#${nav.channelName}`;

  const loading =
    (nav.view === "channels" && channels.isLoading) ||
    (nav.view === "items" && itemsQuery.isLoading);

  return (
    <div className={local.attachPicker} data-testid="file-attach-picker" role="presentation">
      <div className={local.attachPickerHead}>
        {nav.view !== "channels" ? (
          <button
            type="button"
            className={local.attachBack}
            data-testid="file-attach-back"
            onMouseDown={(e) => e.preventDefault()}
            onClick={() => {
              if (!goBack()) onClose();
            }}
          >
            Back
          </button>
        ) : (
          <span />
        )}
        <span className={local.attachPickerTitle}>{title}</span>
        <button
          type="button"
          className={local.attachBack}
          aria-label="Close"
          data-testid="file-attach-close"
          onMouseDown={(e) => e.preventDefault()}
          onClick={onClose}
        >
          Close
        </button>
      </div>
      <ul
        ref={listRef}
        id="file-attach-listbox"
        className={local.attachPickerList}
        role="listbox"
        aria-label={title}
        data-testid="file-attach-list"
      >
        {loading && (
          <li className={local.attachEmpty} role="presentation">
            Loading…
          </li>
        )}
        {!loading && rows.length === 0 && (
          <li className={local.attachEmpty} role="presentation" data-testid="file-attach-empty">
            {nav.view === "channels" ? "No file channels" : "No files here"}
          </li>
        )}
        {!loading &&
          rows.map((row, index) => (
            <li
              key={`${row.kind}-${row.id}`}
              id={`file-attach-option-${row.id}`}
              role="option"
              aria-selected={index === activeIndex}
              data-attach-index={index}
              data-testid={
                row.kind === "channel"
                  ? "file-attach-channel"
                  : row.kind === "folder"
                    ? "file-attach-folder"
                    : "file-attach-option"
              }
              className={
                index === activeIndex
                  ? `${local.fileAttachOption} ${local.fileAttachOptionActive}`
                  : local.fileAttachOption
              }
              onMouseDown={(e) => {
                e.preventDefault();
                activateRow(row);
              }}
              onMouseEnter={() => setActiveIndex(index)}
            >
              <span className={local.attachRowGlyph} aria-hidden="true">
                {row.kind === "channel" ? "#" : row.kind === "folder" ? "▦" : "·"}
              </span>
              <span className={local.attachRowLabel}>{row.label}</span>
              {row.kind === "file" && row.meta && (
                <span className={local.fileOptionMeta}>{row.meta}</span>
              )}
              {(row.kind === "channel" || row.kind === "folder") && (
                <span className={local.attachRowHint}>Open</span>
              )}
            </li>
          ))}
      </ul>
    </div>
  );
});
