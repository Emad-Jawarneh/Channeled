import { useEffect, useRef, useState } from "react";
import { ItemKind, type FileItem } from "../../api/channeled";
import { fileTypeFilterKey, type FileKindCategory } from "../../lib/describeFileKind";
import styles from "./FileThumb.module.css";

type Props = {
  item: FileItem;
  /** Same-origin content URL; required for image/video media thumbs. */
  contentUrl?: string | null;
  size?: "sm" | "lg";
};

function CategoryGlyph({
  category,
}: {
  category: FileKindCategory | "folder" | "other";
}) {
  const common = {
    viewBox: "0 0 24 24",
    fill: "none",
    stroke: "currentColor",
    strokeWidth: 1.7,
    strokeLinecap: "round" as const,
    strokeLinejoin: "round" as const,
    "aria-hidden": true as const,
  };

  switch (category) {
    case "folder":
      return (
        <svg {...common}>
          <path d="M3 7.5A1.5 1.5 0 0 1 4.5 6H9l2 2h8.5A1.5 1.5 0 0 1 21 9.5v8A1.5 1.5 0 0 1 19.5 19h-15A1.5 1.5 0 0 1 3 17.5v-10Z" />
        </svg>
      );
    case "image":
      return (
        <svg {...common}>
          <rect x="3.5" y="5" width="17" height="14" rx="2" />
          <circle cx="9" cy="10" r="1.5" />
          <path d="m6.5 16.5 3.5-3.5 2.5 2.5 3-3 2.5 2.5" />
        </svg>
      );
    case "video":
      return (
        <svg {...common}>
          <rect x="3.5" y="6" width="13" height="12" rx="2" />
          <path d="M16.5 10.5 20.5 8v8l-4-2.5Z" />
        </svg>
      );
    case "audio":
      return (
        <svg {...common}>
          <path d="M9 17a2.5 2.5 0 1 0 0-5 2.5 2.5 0 0 0 0 5Z" />
          <path d="M11.5 12V5.5L19 4v7" />
          <path d="M19 14.5a2.5 2.5 0 1 1-2.5-2.5" />
        </svg>
      );
    case "document":
      return (
        <svg {...common}>
          <path d="M7 3.5h7l4 4V20a1.5 1.5 0 0 1-1.5 1.5H7A1.5 1.5 0 0 1 5.5 20V5A1.5 1.5 0 0 1 7 3.5Z" />
          <path d="M14 3.5V8h4.5" />
          <path d="M8.5 13h7M8.5 16.5h5" />
        </svg>
      );
    case "spreadsheet":
      return (
        <svg {...common}>
          <rect x="4" y="4" width="16" height="16" rx="2" />
          <path d="M4 9.5h16M4 14.5h16M9.5 4v16M14.5 4v16" />
        </svg>
      );
    case "presentation":
      return (
        <svg {...common}>
          <rect x="3.5" y="4.5" width="17" height="11" rx="1.5" />
          <path d="M12 15.5v4M8.5 19.5h7" />
        </svg>
      );
    case "archive":
      return (
        <svg {...common}>
          <path d="M6 8.5h12l-.8 11H6.8L6 8.5Z" />
          <path d="M5 5.5h14v3H5Z" />
          <path d="M12 5.5v3M12 11.5v2M12 15v2" />
        </svg>
      );
    case "code":
      return (
        <svg {...common}>
          <path d="m8 8-4 4 4 4M16 8l4 4-4 4M13 6l-2 12" />
        </svg>
      );
    case "text":
      return (
        <svg {...common}>
          <path d="M7 4h10M12 4v16M8.5 20h7" />
        </svg>
      );
    default:
      return (
        <svg {...common}>
          <path d="M7 3.5h7l4 4V20a1.5 1.5 0 0 1-1.5 1.5H7A1.5 1.5 0 0 1 5.5 20V5A1.5 1.5 0 0 1 7 3.5Z" />
          <path d="M14 3.5V8h4.5" />
        </svg>
      );
  }
}

function mediaKind(item: FileItem): "image" | "video" | null {
  if (item.kind === ItemKind.Folder) return null;
  const key = fileTypeFilterKey(item);
  if (key === "image") return "image";
  if (key === "video") return "video";
  return null;
}

export function FileThumb({ item, contentUrl, size = "sm" }: Props) {
  const category = fileTypeFilterKey(item);
  const media = mediaKind(item);
  const rootRef = useRef<HTMLDivElement | null>(null);
  const [visible, setVisible] = useState(size === "lg");
  const [failed, setFailed] = useState(false);

  useEffect(() => {
    setFailed(false);
  }, [item.id, contentUrl]);

  useEffect(() => {
    if (size === "lg" || !media || !contentUrl) return;
    const el = rootRef.current;
    if (!el) return;
    const io = new IntersectionObserver(
      (entries) => {
        if (entries.some((e) => e.isIntersecting)) {
          setVisible(true);
          io.disconnect();
        }
      },
      { rootMargin: "120px" },
    );
    io.observe(el);
    return () => io.disconnect();
  }, [media, contentUrl, size, item.id]);

  const showMedia = Boolean(media && contentUrl && visible && !failed);

  return (
    <div
      ref={rootRef}
      className={styles.thumb}
      data-size={size}
      data-category={category}
      data-testid={`thumb-${item.name}`}
      aria-hidden="true"
    >
      {showMedia && media === "image" && (
        <img
          className={styles.media}
          src={contentUrl!}
          alt=""
          loading="lazy"
          decoding="async"
          onError={() => setFailed(true)}
        />
      )}
      {showMedia && media === "video" && (
        <>
          <video
            className={styles.media}
            src={contentUrl!}
            muted
            playsInline
            preload="metadata"
            onError={() => setFailed(true)}
            onLoadedData={(e) => {
              try {
                e.currentTarget.currentTime = Math.min(0.1, e.currentTarget.duration || 0.1);
              } catch {
                /* ignore seek failures */
              }
            }}
          />
          <span className={styles.playBadge} aria-hidden="true">
            ▶
          </span>
        </>
      )}
      {!showMedia && (
        <span className={styles.glyph}>
          <CategoryGlyph category={category} />
        </span>
      )}
    </div>
  );
}
