let csrfToken: string | null = null;

export async function ensureCsrf(): Promise<string> {
  if (csrfToken) {
    return csrfToken;
  }
  const response = await fetch("/api/v1/auth/csrf", { credentials: "same-origin" });
  if (!response.ok) {
    throw new Error("Failed to fetch CSRF token");
  }
  const body = (await response.json()) as { token: string };
  csrfToken = body.token;
  return csrfToken;
}

export function clearCsrf(): void {
  csrfToken = null;
}

export class ApiError extends Error {
  status: number;
  code: string;

  constructor(status: number, code: string, message: string) {
    super(message);
    this.name = "ApiError";
    this.status = status;
    this.code = code;
  }
}

type Json = Record<string, unknown> | unknown[] | string | number | boolean | null;

export async function api<T>(
  path: string,
  init: RequestInit & { json?: Json } = {},
): Promise<T> {
  const headers = new Headers(init.headers);
  const method = (init.method ?? "GET").toUpperCase();
  const mutating = method !== "GET" && method !== "HEAD" && method !== "OPTIONS";

  const csrfExempt =
    path.includes("/auth/login") ||
    path.includes("/auth/register") ||
    path.includes("/auth/csrf") ||
    path.includes("/auth/invitations/accept") ||
    path.includes("/auth/forgot-password") ||
    path.includes("/auth/reset-password");
  if (mutating && !csrfExempt) {
    headers.set("X-CSRF-TOKEN", await ensureCsrf());
  }

  let body = init.body;
  if (init.json !== undefined) {
    headers.set("Content-Type", "application/json");
    body = JSON.stringify(init.json);
  }

  const response = await fetch(path, {
    ...init,
    method,
    headers,
    body,
    credentials: "same-origin",
  });

  if (!response.ok) {
    const problem = (await response.json().catch(() => null)) as
      | { code?: string; detail?: string; title?: string }
      | null;
    throw new ApiError(
      response.status,
      problem?.code ?? "error",
      problem?.detail ?? problem?.title ?? `Request failed (${response.status})`,
    );
  }

  if (response.status === 204) {
    return undefined as T;
  }

  const text = await response.text();
  if (!text) {
    return undefined as T;
  }
  return JSON.parse(text) as T;
}
