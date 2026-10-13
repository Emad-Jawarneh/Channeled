import { ItemKind, MemberRole, type FileItem } from "../../api/channeled";

type Opts = {
  item: FileItem;
  myUserId?: string | null;
  /** Team role; ignored when personal. */
  myRole?: number | null;
  personal?: boolean;
};

/**
 * Client gate matching server move rules (Viewer none; Member own files + any folder;
 * Admin/Owner any). Personal owner always.
 */
export function canMoveItem({ item, myUserId, myRole, personal }: Opts): boolean {
  if (personal) return true;
  const role = myRole ?? MemberRole.Viewer;
  if (role < MemberRole.Member) return false;
  if (role >= MemberRole.Admin) return true;
  if (item.kind === ItemKind.Folder) return true;
  return Boolean(myUserId && item.uploadedBy === myUserId);
}
