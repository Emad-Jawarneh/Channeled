/** Formats a byte count as B / KB / MB / GB / TB (base 1024). */
export function formatBytes(bytes: number | null | undefined): string {
  if (bytes == null || Number.isNaN(bytes)) {
    return "—";
  }
  if (bytes < 0) {
    return formatBytes(0);
  }
  if (bytes < 1024) {
    return `${Math.round(bytes)} B`;
  }

  const units = ["KB", "MB", "GB", "TB"] as const;
  let value = bytes / 1024;
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }

  const rounded =
    value >= 100 ? value.toFixed(0) : value.toFixed(1).replace(/\.0$/, "");
  return `${rounded} ${units[unit]}`;
}
