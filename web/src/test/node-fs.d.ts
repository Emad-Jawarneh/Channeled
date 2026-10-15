// The app tsconfig has no @types/node; tests that read fixtures from disk only need this signature.
declare module "node:fs" {
  export function readFileSync(path: string, encoding: "utf8"): string;
}
