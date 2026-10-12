import { useQuery } from "@tanstack/react-query";
import { useState } from "react";
import { searchApi, teamsApi } from "../../api/channeled";
import { AppShell } from "../../components/AppShell";
import { Button } from "../../ui/Button";
import styles from "../files/FileBrowser.module.css";

export function SearchPage() {
  const [q, setQ] = useState("");
  const [submitted, setSubmitted] = useState("");
  const teams = useQuery({ queryKey: ["teams"], queryFn: () => teamsApi.list() });
  const teamSlug = teams.data?.[0]?.slug;

  const results = useQuery({
    queryKey: ["search", submitted, teamSlug],
    enabled: submitted.length > 0,
    queryFn: () =>
      searchApi.search(submitted, teamSlug ? "team" : "personal", teamSlug),
  });

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
      {results.isLoading && <p>Searching…</p>}
      <ul data-testid="search-results">
        {(results.data?.items ?? []).map((item) => (
          <li key={item.id}>{item.name}</li>
        ))}
      </ul>
    </AppShell>
  );
}
