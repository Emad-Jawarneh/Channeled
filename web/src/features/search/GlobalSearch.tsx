import { useQuery } from "@tanstack/react-query";
import { useCallback, useEffect, useId, useRef, useState, type KeyboardEvent as ReactKeyboardEvent } from "react";
import { useLocation, useNavigate, useSearchParams } from "react-router-dom";
import { ItemKind, SearchScope, searchApi, teamsApi, type SearchHit } from "../../api/channeled";
import { useAnnounce } from "../../components/liveAnnouncerContext";
import { useDebouncedValue } from "../../lib/useDebouncedValue";
import { hitLocationLabel } from "./hitLocation";
import styles from "./GlobalSearch.module.css";

const TYPEAHEAD_LIMIT = 8;
const MIN_QUERY = 1;

function SearchGlyph() {
  return (
    <svg viewBox="0 0 24 24" fill="none" aria-hidden="true">
      <circle cx="11" cy="11" r="6.5" stroke="currentColor" strokeWidth="1.8" />
      <path d="M16.2 16.2 20 20" stroke="currentColor" strokeWidth="1.8" strokeLinecap="round" />
    </svg>
  );
}

export function GlobalSearch() {
  const navigate = useNavigate();
  const location = useLocation();
  const [searchParams] = useSearchParams();
  const announce = useAnnounce();
  const inputRef = useRef<HTMLInputElement>(null);
  const rootRef = useRef<HTMLDivElement>(null);
  const listId = useId();
  const [q, setQ] = useState(() =>
    location.pathname === "/search" ? (searchParams.get("q") ?? "") : "",
  );
  const [open, setOpen] = useState(false);
  const [activeIndex, setActiveIndex] = useState(-1);
  const debounced = useDebouncedValue(q.trim(), 200);
  const canSearch = debounced.length >= MIN_QUERY;
  const teams = useQuery({ queryKey: ["teams"], queryFn: () => teamsApi.list() });

  // Adopt `?q=` when landing on /search (e.g. Enter from another page), but not while typing.
  useEffect(() => {
    if (location.pathname !== "/search") return;
    if (document.activeElement === inputRef.current) return;
    setQ(searchParams.get("q") ?? "");
  }, [location.pathname, searchParams]);

  // On the full search page, keep `?q=` in sync so results update as the user types.
  useEffect(() => {
    if (location.pathname !== "/search") return;
    const next = debounced;
    const current = (searchParams.get("q") ?? "").trim();
    if (next === current) return;
    const params = new URLSearchParams();
    if (next) params.set("q", next);
    const search = params.toString();
    navigate({ pathname: "/search", search: search ? `?${search}` : "" }, { replace: true });
  }, [debounced, location.pathname, navigate, searchParams]);

  const results = useQuery({
    queryKey: ["search", "typeahead", SearchScope.Accessible, debounced],
    enabled: canSearch && open && location.pathname !== "/search",
    queryFn: ({ signal }) =>
      searchApi.search(debounced, SearchScope.Accessible, { limit: TYPEAHEAD_LIMIT }, signal),
    placeholderData: (prev) => prev,
  });

  const hits = results.data?.hits ?? [];
  // Typeahead drops away on the dedicated search page — results fill the main pane instead.
  const showPanel = open && q.trim().length >= MIN_QUERY && location.pathname !== "/search";

  useEffect(() => {
    setActiveIndex(-1);
  }, [debounced]);

  useEffect(() => {
    if (!showPanel || !canSearch || results.isFetching) return;
    if (results.isSuccess) {
      announce(
        hits.length === 0
          ? `No results for ${debounced}`
          : `${hits.length} result${hits.length === 1 ? "" : "s"} for ${debounced}`,
      );
    }
  }, [showPanel, canSearch, results.isFetching, results.isSuccess, hits.length, debounced, announce]);

  useEffect(() => {
    function onKeyDown(event: KeyboardEvent) {
      if (!(event.metaKey || event.ctrlKey) || event.key.toLowerCase() !== "k") return;
      const target = event.target as HTMLElement | null;
      if (target?.closest("input, textarea, [contenteditable='true']") && target !== inputRef.current) {
        return;
      }
      event.preventDefault();
      inputRef.current?.focus();
      setOpen(true);
    }
    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, []);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(event: MouseEvent) {
      if (!rootRef.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    }
    document.addEventListener("mousedown", onPointerDown);
    return () => document.removeEventListener("mousedown", onPointerDown);
  }, [open]);

  const openHit = useCallback(
    (hit: SearchHit) => {
      setOpen(false);
      setQ("");
      navigate(`/open/${encodeURIComponent(hit.id)}`);
    },
    [navigate],
  );

  const goToFullResults = useCallback(() => {
    const query = q.trim();
    if (!query) return;
    setOpen(false);
    navigate(`/search?q=${encodeURIComponent(query)}`);
  }, [navigate, q]);

  function onKeyDown(event: ReactKeyboardEvent<HTMLInputElement>) {
    if (event.key === "Escape") {
      if (q) {
        setQ("");
      } else {
        setOpen(false);
        inputRef.current?.blur();
      }
      event.preventDefault();
      return;
    }
    if (event.key === "ArrowDown") {
      if (!showPanel || hits.length === 0) return;
      event.preventDefault();
      setActiveIndex((i) => (i + 1) % hits.length);
      return;
    }
    if (event.key === "ArrowUp") {
      if (!showPanel || hits.length === 0) return;
      event.preventDefault();
      setActiveIndex((i) => (i <= 0 ? hits.length - 1 : i - 1));
      return;
    }
    if (event.key === "Enter") {
      event.preventDefault();
      if (activeIndex >= 0 && hits[activeIndex]) {
        openHit(hits[activeIndex]);
      } else {
        goToFullResults();
      }
    }
  }

  const activeId = activeIndex >= 0 ? `${listId}-opt-${activeIndex}` : undefined;

  return (
    <div className={styles.root} ref={rootRef} data-testid="global-search">
      <label className={styles.field}>
        <span className={styles.icon}>
          <SearchGlyph />
        </span>
        <span className="visually-hidden">Search files and folders</span>
        <input
          ref={inputRef}
          className={styles.input}
          data-testid="search-input"
          type="search"
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={showPanel}
          aria-controls={showPanel ? listId : undefined}
          aria-activedescendant={activeId}
          placeholder="Search files…"
          value={q}
          autoComplete="off"
          onChange={(e) => {
            setQ(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
        />
        <kbd className={styles.kbd} aria-hidden="true">
          {typeof navigator !== "undefined" && /Mac|iPhone|iPad/.test(navigator.platform) ? "⌘K" : "Ctrl K"}
        </kbd>
      </label>

      {showPanel && (
        <div className={styles.panel} role="presentation">
          {results.isFetching && hits.length === 0 && (
            <p className={styles.status} role="status">
              Searching…
            </p>
          )}
          {results.isError && (
            <p className={styles.status} role="alert">
              Search failed. Try again.
            </p>
          )}
          {canSearch && results.isSuccess && hits.length === 0 && !results.isFetching && (
            <p className={styles.status} role="status">
              No matches.
            </p>
          )}
          {hits.length > 0 && (
            <ul id={listId} className={styles.list} role="listbox" aria-label="Search results">
              {hits.map((hit, index) => (
                <li key={hit.id} role="presentation">
                  <button
                    type="button"
                    id={`${listId}-opt-${index}`}
                    role="option"
                    aria-selected={index === activeIndex}
                    data-active={index === activeIndex ? "true" : undefined}
                    data-testid={`search-hit-${hit.name}`}
                    className={styles.hit}
                    onMouseEnter={() => setActiveIndex(index)}
                    onClick={() => openHit(hit)}
                  >
                    <span className={styles.hitKind} aria-hidden="true">
                      {hit.kind === ItemKind.Folder ? "Fo" : "Fi"}
                    </span>
                    <span className={styles.hitName}>{hit.name}</span>
                    <span className={styles.hitWhere}>{hitLocationLabel(hit, teams.data)}</span>
                  </button>
                </li>
              ))}
            </ul>
          )}
          {canSearch && (
            <button type="button" className={styles.footer} data-testid="search-view-all" onClick={goToFullResults}>
              View all results
            </button>
          )}
        </div>
      )}
    </div>
  );
}
