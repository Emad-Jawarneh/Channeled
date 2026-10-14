type Props = {
  rows?: number;
  className?: string;
};

/** Cheap transform-only shimmer placeholders for file lists. */
export function SkeletonList({ rows = 6, className }: Props) {
  return (
    <ul className={["skeleton-list", className].filter(Boolean).join(" ")} data-testid="skeleton-list" aria-busy="true" aria-label="Loading">
      {Array.from({ length: rows }, (_, i) => (
        <li key={i} className="skeleton-row">
          <span className="skeleton" style={{ width: "1rem", height: "1rem", borderRadius: 4 }} />
          <span className="skeleton" style={{ width: `${55 + ((i * 7) % 30)}%` }} />
          <span className="skeleton" style={{ width: "4rem" }} />
          <span className="skeleton" style={{ width: "3rem" }} />
          <span className="skeleton" style={{ width: "1.5rem" }} />
        </li>
      ))}
    </ul>
  );
}
