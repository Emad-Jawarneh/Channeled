import { useInfiniteQuery } from "@tanstack/react-query";
import { useState } from "react";
import { Link } from "react-router-dom";
import { ItemKind, SearchScope, searchApi, type SearchHit } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";

function hitLocation(hit: SearchHit): { label: string; to: string } {
  if (hit.teamSlug && hit.channelName) {
    return {
      label: `${hit.teamSlug} · #${hit.channelName}`,
      to: `/t/${hit.teamSlug}/c/${hit.channelName}`,
    };
  }
  return { label: "My files", to: "/me/files" };
}

export function SearchPage() {
  const [q, setQ] = useState("");
  const [submitted, setSubmitted] = useState("");

  // Accessible scope: every file the user can currently see across all teams plus their own files.
  const results = useInfiniteQuery({
    queryKey: ["search", SearchScope.Accessible, submitted],
    enabled: submitted.length > 0,
    initialPageParam: null as string | null,
    queryFn: ({ pageParam }) =>
      searchApi.search(submitted, SearchScope.Accessible, { cursor: pageParam }),
    getNextPageParam: (last) => last.nextCursor ?? null,
  });

  const hits = (results.data?.pages ?? []).flatMap((p) => p.hits ?? []);

  return (
    <AppShell title="Search">
      <form
        className={styles.toolbar}
        onSubmit={(e) => {
          e.preventDefault();
          setSubmitted(q.trim());
        }}
      >
        <label>
          Query
          <input
            data-testid="search-input"
            value={q}
            onChange={(e) => setQ(e.target.value)}
            required
          />
        </label>
        <Button type="submit" variant="primary">
          Search
        </Button>
      </form>
      {results.isLoading && <p className={styles.lede}>Searching…</p>}
      {results.isError && (
        <p className={styles.lede} role="alert">
          Search failed. Try again.
        </p>
      )}
      <ul data-testid="search-results" className={styles.resultList}>
        {hits.map((hit) => {
          const where = hitLocation(hit);
          return (
            <li key={`${hit.id}`}>
              <Link to={where.to}>
                {hit.kind === ItemKind.Folder ? "📁 " : ""}
                {hit.name}
              </Link>{" "}
              <span className={styles.lede}>{where.label}</span>
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
      {submitted && results.isSuccess && hits.length === 0 && <p className={styles.lede}>No matches.</p>}
    </AppShell>
  );
}
