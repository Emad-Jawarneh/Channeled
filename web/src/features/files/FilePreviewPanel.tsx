import { useEffect, useState } from "react";
import { ItemKind, type FileItem } from "../../api/channeled";
import { describeFileKind, fileTypeFilterKey, formatFileType } from "../../lib/describeFileKind";
import { formatBytes } from "../../lib/formatBytes";
import { Button } from "../../ui/Button";
import { FileThumb } from "./FileThumb";
import styles from "./FilePreviewPanel.module.css";

type Props = {
  item: FileItem | null;
  contentUrl?: string | null;
  showDownload?: boolean;
  onDownload?: () => void;
};

function mediaKind(item: FileItem): "image" | "video" | "audio" | null {
  if (item.kind === ItemKind.Folder) return null;
  const key = fileTypeFilterKey(item);
  if (key === "image") return "image";
  if (key === "video") return "video";
  if (key === "audio") return "audio";
  return null;
}

export function FilePreviewPanel({
  item,
  contentUrl,
  showDownload = false,
  onDownload,
}: Props) {
  const [mediaFailed, setMediaFailed] = useState(false);

  useEffect(() => {
    setMediaFailed(false);
  }, [item?.id, contentUrl]);

  if (!item) {
    return (
      <div className={styles.panel} data-testid="details-panel" data-empty="true">
        <div className={styles.empty}>
          <p className={styles.emptyTitle}>No selection</p>
          <p className={styles.emptyHint}>Select a file or folder to preview it here.</p>
        </div>
      </div>
    );
  }

  const category = fileTypeFilterKey(item);
  const media = mediaKind(item);
  const showMedia = Boolean(media && contentUrl && !mediaFailed);
  const modified = new Date(item.updatedAt);

  return (
    <div className={styles.panel} data-testid="details-panel" data-category={category}>
      <div className={styles.stage} data-media={showMedia ? media : "glyph"}>
        {showMedia && media === "image" && (
          <img
            className={styles.media}
            src={contentUrl!}
            alt=""
            decoding="async"
            onError={() => setMediaFailed(true)}
          />
        )}
        {showMedia && media === "video" && (
          <video
            className={styles.media}
            src={contentUrl!}
            controls
            playsInline
            preload="metadata"
            onError={() => setMediaFailed(true)}
          />
        )}
        {showMedia && media === "audio" && (
          <div className={styles.audioStage}>
            <FileThumb item={item} size="lg" />
            <audio
              className={styles.audio}
              src={contentUrl!}
              controls
              preload="metadata"
              onError={() => setMediaFailed(true)}
            />
          </div>
        )}
        {!showMedia && (
          <div className={styles.glyphStage}>
            <FileThumb item={item} contentUrl={null} size="lg" />
          </div>
        )}
      </div>

      <div className={styles.body}>
        <h2 className={styles.name} title={item.name}>
          {item.name}
        </h2>
        <p className={styles.type}>{formatFileType(item)}</p>

        <dl className={styles.meta}>
          <div>
            <dt>Kind</dt>
            <dd>{describeFileKind(item)}</dd>
          </div>
          {item.kind !== ItemKind.Folder && item.sizeBytes != null && (
            <div>
              <dt>Size</dt>
              <dd>{formatBytes(item.sizeBytes)}</dd>
            </div>
          )}
          <div>
            <dt>Modified</dt>
            <dd>
              <time dateTime={item.updatedAt}>
                {modified.toLocaleDateString(undefined, {
                  year: "numeric",
                  month: "short",
                  day: "numeric",
                })}
                <span className={styles.metaTime}>
                  {modified.toLocaleTimeString(undefined, {
                    hour: "2-digit",
                    minute: "2-digit",
                  })}
                </span>
              </time>
            </dd>
          </div>
        </dl>

        {showDownload && item.kind === ItemKind.File && onDownload && (
          <div className={styles.actions}>
            <Button
              type="button"
              variant="primary"
              data-testid="details-download"
              onClick={onDownload}
            >
              Download
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}
