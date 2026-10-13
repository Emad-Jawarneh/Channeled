function clickAnchor(url: string, fileName: string): void {
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  a.rel = "noopener";
  document.body.appendChild(a);
  a.click();
  a.remove();
}

/**
 * Download a same-origin (or blob:/data:) file.
 * Authenticated API paths are fetched with cookies, then saved via a blob URL —
 * a bare &lt;a download href="/api/…"&gt; often fails in Chrome ("Site wasn't available"),
 * especially for HTML and other document types through the Vite proxy.
 */
export async function triggerDownload(url: string, fileName: string): Promise<void> {
  if (url.startsWith("blob:") || url.startsWith("data:")) {
    clickAnchor(url, fileName);
    return;
  }

  const response = await fetch(url, { credentials: "same-origin" });
  if (!response.ok) {
    let message = `Download failed (${response.status})`;
    try {
      const problem = (await response.json()) as { detail?: string; title?: string } | null;
      message = problem?.detail ?? problem?.title ?? message;
    } catch {
      /* ignore non-JSON error bodies */
    }
    throw new Error(message);
  }

  const blob = await response.blob();
  const objectUrl = URL.createObjectURL(blob);
  try {
    clickAnchor(objectUrl, fileName);
  } finally {
    // Keep the blob URL alive briefly so the browser can start the download.
    window.setTimeout(() => URL.revokeObjectURL(objectUrl), 60_000);
  }
}

/** Stagger multiple downloads so the browser does not coalesce/block them. */
export async function triggerDownloads(
  items: { url: string; fileName: string }[],
  gapMs = 200,
): Promise<void> {
  for (let i = 0; i < items.length; i++) {
    const item = items[i]!;
    await triggerDownload(item.url, item.fileName);
    if (i < items.length - 1) {
      await new Promise((r) => setTimeout(r, gapMs));
    }
  }
}
