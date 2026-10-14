import { useQuery } from "@tanstack/react-query";
import { Link } from "react-router-dom";
import { filesApi } from "../../api/channeled";
import {
  hrefForResolvedFile,
  labelForResolvedFile,
  normalizeComposerFileIds,
} from "./messageFileRefs";
import local from "./TextChannelPage.module.css";

type Props = {
  teamSlug: string;
  fileIds: string[];
};

export function MessageFileChips({ teamSlug, fileIds }: Props) {
  const ids = normalizeComposerFileIds(fileIds);
  const resolved = useQuery({
    queryKey: ["file-resolve", teamSlug, ids],
    queryFn: () => filesApi.resolve(teamSlug, ids),
    enabled: ids.length > 0,
    staleTime: 60_000,
  });

  if (ids.length === 0) return null;

  const byId = new Map((resolved.data?.items ?? []).map((item) => [item.fileId, item]));

  return (
    <ul className={local.fileChipList} data-testid="message-file-chips">
      {ids.map((id) => {
        const item = byId.get(id);
        const label = item ? labelForResolvedFile(item) : "File";
        const muted = item && (!item.accessible || item.state !== "live");
        const href = item ? hrefForResolvedFile(teamSlug, item) : null;
        const className = muted ? `${local.fileChip} ${local.fileChipMuted}` : local.fileChip;
        return (
          <li key={id}>
            {href ? (
              <Link
                className={className}
                to={href}
                data-testid="message-file-chip"
                title={item?.channelName ? `Open in #${item.channelName}` : "Open file"}
              >
                {label}
              </Link>
            ) : (
              <span className={className} data-testid="message-file-chip">
                {label}
              </span>
            )}
          </li>
        );
      })}
    </ul>
  );
}
