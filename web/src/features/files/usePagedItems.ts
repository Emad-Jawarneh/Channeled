import { useInfiniteQuery } from "@tanstack/react-query";
import { useMemo } from "react";
import type { ItemList } from "../../api/channeled";
import { ApiError } from "../../api/http";
import { mergePages } from "./pagedItems";

/**
 * Cursor-paged folder listing with "Load more". Invalidating the key (realtime `filesChanged`, uploads,
 * mutations) refetches every loaded page in order, so the user keeps their place.
 */
export function usePagedItems(
  queryKey: readonly unknown[],
  fetchPage: (cursor: string | null, signal: AbortSignal) => Promise<ItemList>,
  enabled = true,
) {
  const query = useInfiniteQuery({
    queryKey,
    enabled,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) => fetchPage(pageParam, signal),
    getNextPageParam: (last) => last.nextCursor ?? null,
    retry: (count, err) => !(err instanceof ApiError && err.status === 404) && count < 2,
  });
  const items = useMemo(() => mergePages(query.data?.pages), [query.data]);
  const path = query.data?.pages[0]?.path ?? null;
  return {
    query,
    items,
    path,
    hasMore: Boolean(query.hasNextPage),
    loadingMore: query.isFetchingNextPage,
    loadMore: () => query.fetchNextPage(),
  };
}
