/** Start a same-origin file download without loading the body into JS memory. */
export function triggerDownload(url: string, fileName: string): void {
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
}

/** Stagger multiple downloads so the browser does not coalesce/block them. */
export async function triggerDownloads(
  items: { url: string; fileName: string }[],
  gapMs = 200,
): Promise<void> {
  for (let i = 0; i < items.length; i++) {
    const item = items[i]!;
    triggerDownload(item.url, item.fileName);
    if (i < items.length - 1) {
      await new Promise((r) => setTimeout(r, gapMs));
    }
  }
}
