import type { paths } from "./generated";

/** Typed fetch helpers will grow with OpenAPI operations. */
export type ApiPaths = paths;

export async function getApiHealth(): Promise<{ status: string }> {
  const response = await fetch("/api/v1/health", { credentials: "same-origin" });
  if (!response.ok) {
    throw new Error(`API health failed (${response.status})`);
  }
  return response.json() as Promise<{ status: string }>;
}
