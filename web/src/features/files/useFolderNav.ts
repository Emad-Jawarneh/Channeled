import { useCallback, useMemo } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";

const GUID_RE =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isGuid(value: string): boolean {
  return GUID_RE.test(value);
}

export type FolderNavState = {
  folderName?: string | null;
};

/**
 * URL `?folder=<guid>` is the source of truth for the current folder leaf.
 * Absent (or disabled) means root. History uses push so Back walks up.
 * Optional `folderName` is carried in location state as a crumb fallback until path loads.
 */
export function useFolderNav(enabled: boolean) {
  const [params] = useSearchParams();
  const navigate = useNavigate();
  const location = useLocation();

  const folderId = useMemo(() => {
    if (!enabled) return undefined;
    const raw = params.get("folder");
    if (!raw || !isGuid(raw)) return undefined;
    return raw;
  }, [enabled, params]);

  const folderNameHint = useMemo(() => {
    const state = location.state as FolderNavState | null;
    return state?.folderName ?? undefined;
  }, [location.state]);

  const setFolderParam = useCallback(
    (id: string | null, replace: boolean, folderName?: string | null) => {
      const next = new URLSearchParams(location.search);
      const current = next.get("folder");
      if (id) {
        if (current === id && !folderName) return;
        next.set("folder", id);
      } else {
        if (!current) return;
        next.delete("folder");
      }
      const qs = next.toString();
      navigate(
        {
          pathname: location.pathname,
          search: qs ? `?${qs}` : "",
        },
        {
          replace,
          state: id ? ({ folderName: folderName ?? null } satisfies FolderNavState) : null,
        },
      );
    },
    [navigate, location.pathname, location.search],
  );

  const openFolder = useCallback(
    (id: string, name?: string) => {
      if (!enabled) return;
      setFolderParam(id, false, name);
    },
    [enabled, setFolderParam],
  );

  const goToRoot = useCallback(
    (opts?: { replace?: boolean }) => {
      setFolderParam(null, opts?.replace ?? false);
    },
    [setFolderParam],
  );

  const goToSegment = useCallback(
    (id: string | null, name?: string) => {
      if (!enabled) return;
      if (id == null) goToRoot();
      else openFolder(id, name);
    },
    [enabled, goToRoot, openFolder],
  );

  /** Clear an invalid/stale `?folder=` without adding history. */
  const clearInvalidFolder = useCallback(() => {
    setFolderParam(null, true);
  }, [setFolderParam]);

  return {
    folderId,
    folderNameHint,
    openFolder,
    goToRoot,
    goToSegment,
    clearInvalidFolder,
  };
}
