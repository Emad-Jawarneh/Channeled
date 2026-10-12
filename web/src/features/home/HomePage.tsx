import { useQuery } from "@tanstack/react-query";
import { Button } from "../../ui/Button";
import { AppShell } from "../../components/AppShell";

async function fetchHealth(): Promise<{ status: string }> {
  const response = await fetch("/api/v1/health", { credentials: "same-origin" });
  if (!response.ok) {
    throw new Error(`Health check failed (${response.status})`);
  }
  return response.json() as Promise<{ status: string }>;
}

export function HomePage() {
  const health = useQuery({
    queryKey: ["health"],
    queryFn: fetchHealth,
    retry: false,
  });

  return (
    <AppShell>
      <h1>Channeled</h1>
      <p>Self-hosted multi-team file sharing. M0 foundations scaffold.</p>
      <p>
        API health:{" "}
        {health.isLoading && "checking…"}
        {health.isError && "unreachable (start the API)"}
        {health.data && health.data.status}
      </p>
      <Button variant="primary" type="button" disabled>
        Sign in (M1)
      </Button>
    </AppShell>
  );
}
