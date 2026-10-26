# 04 — Identity & sessions

## Authentication model

- **ASP.NET Core Identity** with EF stores (`AppUser` / `AppRole`).
- **Cookie authentication** only for the SPA/API (no JWT bearer for the browser app).
- Cookie name: `channeled.auth` — HttpOnly, SameSite=Lax, sliding expiration (14 days configured in DI).
- Passwords: Identity hasher; domain `PasswordRules` enforce minimum length (12).
- Lockout after repeated failures; auth endpoints rate-limited (`RateLimits:AuthPermitPerMinute`).

There is **no** machine-to-machine API key surface in MVP.

## CSRF

- Antiforgery cookie: `channeled.csrf`.
- Client reads token from `GET /api/v1/auth/csrf` and sends `X-CSRF-TOKEN` on mutating requests.
- Middleware validates unsafe methods with narrow exemptions (login/register/forgot-password/csrf itself, selected token-bearing account posts, public share claims when applicable).

Pattern used in tests and curl examples in the root README.

## Server-side sessions

On login, the API creates a **session row** and puts a session id claim (`sid`) into the principal.

| Concern | Behavior |
|---------|----------|
| Validation | `SessionCookieEvents.ValidatePrincipalAsync` checks `RevokedAt == null` |
| Cache | Short TTL (`Account:SessionCacheTtlSeconds`, must be ≤ 5) |
| Security stamp | Validated frequently (≈5s); revoke bumps stamp |
| List / revoke | `GET/DELETE /api/v1/auth/sessions…` |
| Side effects of revoke | Cancels pending email-change + unused password-reset tokens; may push auth-state via SignalR |

Logout and password change paths revoke or invalidate sessions as appropriate (`SessionSecurity`).

## Account lifecycle

| Flow | Notes |
|------|-------|
| Self-registration | Open when configured; creates non-admin user |
| Seed operator | CLI `seed-operator` — **one** platform admin bootstrap |
| Ensure user | CLI `ensure-user` with optional `--admin` |
| Password reset | Hashed single-use tokens; expiry; rate limits per address |
| Email change | Confirm + cancel tokens; pending window (`Account:EmailChangePendingMinutes`) |
| Username | Slug allocation; cooldown (`Account:UsernameCooldownDays`) |
| Avatar | Processed in-process (SkiaSharp); stored as object key on user |
| Disable / enable | Admin; enable also cancels early-phase user delete (see deletion doc) |

Mail sending: real SMTP when `Operations:Smtp*` configured; otherwise logging sender. `Account:SelfServiceMail` gates whether self-service mail is attempted.

## Principal → user id

Endpoints resolve the signed-in user from claims and pass `Guid userId` into Application/Infrastructure services. Platform-admin checks are separate (`IsPlatformAdmin` + active status) inside admin services — non-admins receive **404** on admin routes.

## Security notes (identity)

- No auth secrets in `localStorage`.
- Token-based account POSTs that are CSRF-exempt still require the secret token in the body and are rate-limited per IP and per token.
- Pilot deployments may omit 2FA; operators should use HTTPS, lockout, and session revoke (see `docs/SECURITY.md`).
