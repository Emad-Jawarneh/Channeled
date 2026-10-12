# OWASP ASVS Level 2 — Channeled checklist

Filled for M8 against the implemented API + SPA. Status: **Pass** / **Partial** / **N/A**.

| ASVS | Control | Status | Evidence / notes |
|------|---------|--------|------------------|
| V1.1 | Secure SDLC | Pass | Milestone gates, fail-closed `./ci.sh`, tests with features |
| V1.2 | Threat modeling | Partial | Domain threats covered in AGENTS (quota, deny-by-default, audit); formal model TBD |
| V2.1 | Password length/complexity | Pass | Min length 12 (`PasswordRules`) |
| V2.2 | Adaptive hashing | Pass | ASP.NET Identity PasswordHasher |
| V2.3 | Credential recovery | Pass | Hashed single-use reset tokens with expiry |
| V2.4 | Credential storage | Pass | No plaintext passwords; Identity stores hashes |
| V2.5 | Credential change | Pass | Change-password + admin reset audited |
| V2.7 | Resistance to brute force | Pass | Lockout after 5 failures; auth rate limiter |
| V2.8 | Authentication strength | Pass | Session cookies (HttpOnly, SameSite=Lax) |
| V2.10 | Service authentication | N/A | No machine-to-machine API keys in MVP |
| V3.1 | Session management | Pass | Server-side revocable sessions; list/revoke |
| V3.2 | Session binding | Pass | Cookie auth tied to Identity security stamp |
| V3.3 | Session termination | Pass | Logout + per-session revoke |
| V3.4 | Cookie attributes | Pass | HttpOnly; SecurePolicy SameAsRequest; Lax |
| V3.5 | CSRF | Pass | Antiforgery header `X-CSRF-TOKEN` on mutations |
| V4.1 | General access control | Pass | Single `IAuthorizer`; deny→404 for hidden resources |
| V4.2 | Operation-level ACL | Pass | Role × visibility matrix tested (M2) |
| V4.3 | Other access control | Pass | Personal space owner-only (incl. platform admin) |
| V5.1 | Input validation | Pass | DTOs + domain validation; RFC 7807 codes |
| V5.2 | Sanitization | Pass | JSON APIs; SPA renders text nodes (React) |
| V5.3 | Output encoding | Pass | React default escaping |
| V5.4 | Memory safety | Pass | Managed .NET / TypeScript |
| V6.1 | Stored cryptography | Pass | SHA-256 content hashes; TLS at reverse proxy |
| V6.2 | Algorithm suite | Pass | Identity hasher; SHA-256 for files |
| V6.3 | Random values | Pass | Cryptographic tokens for invites/resets |
| V6.4 | Secret management | Pass | Env overrides; no secrets in repo (`channeled.env.example`) |
| V7.1 | Log content | Pass | Structured audit_events; correlation ID header |
| V7.2 | Log protection | Pass | Append-only audit (trigger + REVOKE) |
| V7.3 | Security logging | Pass | Auth failures, admin actions, file lifecycle |
| V7.4 | Log analysis | Partial | Export CSV/JSON; SIEM integration operator-side |
| V8.1 | Data protection | Pass | Quotas, trash retention, backups with verification |
| V8.2 | Client-side storage | Pass | No secrets in localStorage; cookie sessions |
| V8.3 | Sensitive data | Pass | Passwords never logged; CSRF tokens short-lived |
| V9.1 | Communications | Pass | HSTS/headers documented for Caddy/nginx samples |
| V9.2 | TLS | Pass | Terminated at reverse proxy (ops samples) |
| V10.1 | Malicious code | Pass | Dependency audits in CI (`dotnet` vulnerable + `npm audit --audit-level=high`) |
| V10.2 | Integrity | Pass | Upload SHA-256 verify; backup manifest checksums |
| V10.3 | Application integrity | Partial | No code-signing of releases yet |
| V11.1 | Business logic | Pass | Quota enforcement; ownership transfer rules |
| V12.1 | File upload | Pass | Multipart via API; type/size limits; authz on write |
| V12.2 | File download | Pass | Authz + optional Range; download audit hook |
| V12.3 | SSRF / untrusted fetch | Pass | No user-controlled server-side URL fetch |
| V12.4 | File storage | Pass | Object keys scoped; filesystem path traversal guards |
| V13.1 | API generic | Pass | `/api/v1`, problem+json, cursor pagination |
| V13.2 | RESTful | Pass | Resource-oriented routes; CSRF on unsafe methods |
| V13.3 | GraphQL | N/A | Not used |
| V14.1 | Build | Pass | Pinned package versions; CI format/lint/tests |
| V14.2 | Dependency | Pass | Audits fail on high/critical npm and vulnerable NuGet |
| V14.3 | Unintended | Pass | OpenAPI published; admin routes platform-admin-only |
| V14.4 | HTTP security headers | Partial | Proxy samples set HSTS/CSP-related headers; SPA CSP hardening continues in reverse proxy config |
| V14.5 | Validate HTTP | Pass | Size limits; rate limits on auth |

## Residual risks

- Full CSP for SPA should be enforced at the edge in production (documented in ops samples).
- Load test full 2 TB object corpus is operator-run; CI uses a reduced concurrency/latency profile.
- Formal threat model document can be expanded beyond AGENTS narrative.

## Sign-off

M8 security review completed against ASVS L2 controls above for the Channeled codebase as of the M8 gate.
