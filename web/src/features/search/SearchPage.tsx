import { useInfiniteQuery, useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { ItemKind, SearchScope, searchApi, teamsApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { useAnnounce } from "../../components/liveAnnouncerContext";
import { useDebouncedValue } from "../../lib/useDebouncedValue";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";
import { hitLocationLabel } from "./hitLocation";

export function SearchPage() {
  const [params] = useSearchParams();
  const q = (params.get("q") ?? "").trim();
  const debounced = useDebouncedValue(q, 200);
  const announce = useAnnounce();
  const teams = useQuery({ queryKey: ["teams"], queryFn: () => teamsApi.list() });

  const results = useInfiniteQuery({
    queryKey: ["search", SearchScope.Accessible, debounced],
    enabled: debounced.length > 0,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam, signal }) =>
      searchApi.search(debounced, SearchScope.Accessible, { cursor: pageParam }, signal),
    getNextPageParam: (last) => last.nextCursor ?? null,
  });

  const hits = (results.data?.pages ?? []).flatMap((p) => p.hits ?? []);

  useEffect(() => {
    if (!debounced || results.isFetching) return;
    if (results.isSuccess) {
      announce(
        hits.length === 0
          ? `No results for ${debounced}`
          : `${hits.length} result${hits.length === 1 ? "" : "s"} for ${debounced}`,
      );
    }
  }, [debounced, results.isFetching, results.isSuccess, hits.length, announce]);

  return (
    <AppShell title="Search">
      {!q && <p className={styles.lede}>Type in the search bar above to find files and folders.</p>}
      {q && results.isLoading && <p className={styles.lede}>Searching…</p>}
      {q && results.isError && (
        <p className={styles.lede} role="alert">
          Search failed. Try again.
        </p>
      )}
      <ul data-testid="search-results" className={styles.resultList}>
        {hits.map((hit) => {
          const label = hitLocationLabel(hit, teams.data);
          return (
            <li key={hit.id}>
              <Link to={`/open/${encodeURIComponent(hit.id)}`} data-testid={`search-result-${hit.name}`}>
                {hit.kind === ItemKind.Folder ? "Folder · " : ""}
                {hit.name}
              </Link>{" "}
              <span className={styles.lede}>{label}</span>
            </li>
          );
        })}
      </ul>
      {results.hasNextPage && (
        <Button
          type="button"
          data-testid="search-more"
          disabled={results.isFetchingNextPage}
          onClick={() => void results.fetchNextPage()}
        >
          {results.isFetchingNextPage ? "Loading…" : "Load more"}
        </Button>
      )}
      {debounced && results.isSuccess && hits.length === 0 && <p className={styles.lede}>No matches.</p>}
    </AppShell>
  );
}
