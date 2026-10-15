import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { itemsApi, type FileItem } from "../../api/channeled";
import { focusIdFor, locationMatchesView, pathForLocation, type ListView } from "./locate";

/**
 * Resolves the row a deep link asks for (`?item=`). If it is in the loaded pages it is used directly; otherwise
 * the locate call returns it and it is pinned above the list until a later page loads it. If it lives
 * somewhere else (moved, trashed, other folder) the page navigates there.
 */
export function useLocatedItem(opts: {
  focusId: string | null;
  loadedItems: readonly FileItem[];
  listReady: boolean;
  view: ListView;
}): { target: FileItem | null; pinned: FileItem | null; missing: boolean } {
  const { focusId, loadedItems, listReady, view } = opts;
  const navigate = useNavigate();
  const inLoaded = focusId ? (loadedItems.find((i) => i.id === focusId) ?? null) : null;
  const located = useQuery({
    queryKey: ["locate", focusId],
    queryFn: ({ signal }) => itemsApi.locate(focusId!, signal),
    enabled: Boolean(focusId) && listReady && !inLoaded,
    retry: false,
    staleTime: 30_000,
  });

  const location = located.data;
  const belongsHere = location ? locationMatchesView(location, view) : false;
  useEffect(() => {
    if (location && !belongsHere) {
      navigate(pathForLocation(location), { replace: true });
    }
  }, [location, belongsHere, navigate]);

  const pinned =
    !inLoaded && location && belongsHere && focusIdFor(location) === focusId ? location.item : null;
  return { target: inLoaded ?? pinned, pinned, missing: located.isError };
}
