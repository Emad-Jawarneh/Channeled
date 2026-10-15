# Auth hero pane

Status: done
Base: `ff7c164953720adf5dd9e371895e4e5f6f13dd5d` (`ff7c164 Fix search`)

`docs/references/plan-template.md` is not in this repo. This plan follows section 6 of `docs/design.md`: one change, self-contained, existing tokens only.

## Change

Make the shared login/signup hero match the left pane of the Sign in frame in `docs/design/Channeled – UI Mockups.html`.

That pane is solid teal, with contrast-colored type, check marks, and two overlapping preview cards. Each page keeps its own headline and lede.

## Contract

`docs/DECISIONS.md`, “2026-10-12 — UI mockup system”: the SPA visual system follows `docs/design/Channeled – UI Mockups.html`, including split auth screens. Those mockups are the source of truth for layout and brand.

The Sign in frame (board title “Sign in”) paints the left pane as:

- `flex: 1 1 520px`
- `background: var(--sf)` → token `--color-accent` (`#66acb7`)
- `color: #000` → token `--color-accent-contrast` (`#000000` in light theme, `#061014` in dark theme)
- `padding: 56px 64px`
- column flex
- brand mark: 40×40, radius 12px, `background: var(--acc)` → `--color-accent-strong`, glyph `#fff` → `--color-on-accent-strong`
- title block `margin-top: 56px`
- title: weight 700, size 42px, line-height 1.1, letter-spacing -0.02em, `max-width: 500px`, margin `0 0 14px`
- lede: 16px, `max-width: 460px`, same pane color (not muted)
- decorative cluster, `height: 300px`, `margin: 24px 0`, `aria-hidden`
- points pinned with `margin-top: auto`, column, `gap: 10px`, each row a check icon plus label

Do not apply the frame’s invitation copy. `docs/DECISIONS.md`, “2026-10-12 — Open self-registration”, keeps open signup. Leave form headings, hints, and footers as they are.

`docs/DECISIONS.md`, “2026-10-13 — Dark mode”: theme is a token swap. Use the tokens above. Keep the existing `ThemeToggle` in the hero. Do not add a second style tree.

## Current runtime

Only these two views render `LoginPage.module.css` `.hero`:

- `web/src/features/login/LoginPage.tsx` — headline “Share files with your teams, on your own server.” Lede “Organized by team and channel, with clear permissions on every file.”
- `web/src/features/signup/SignupPage.tsx` — imports `../login/LoginPage.module.css`. Headline “Create your account and start sharing.” Lede “Self-hosted file sharing for teams — channels, personal space, and audit you control.”

`.hero` is a light `--gradient-teal-soft` wash plus a blurred `.hero::after`. `.heroLede` is `--color-text-muted`. `.heroPoints p` is `--color-text` with an 8px `--color-accent` disc from `::before`. There is no preview cluster. `.logo` uses `--color-accent-strong` with `--color-bg` for the glyph.

The same stylesheet’s `.page`, `.pageCentered`, `.form`, `.panel`, `.label`, `.input`, `.hint`, `.footer`, and `.heroTop` are also used by reset, invite, share, join, and email-confirmation pages. Those pages do not render `.hero`. Do not change their layout.

`web/src/features/landing/LandingPage.tsx` has its own `.hero` in `LandingPage.module.css`. Leave it alone.

## Implementation

Add `web/src/features/login/AuthHero.tsx` and use it from `LoginPage.tsx` and `SignupPage.tsx`. Pass the existing headline and lede as props. Do not put this in `web/src/ui/`. `FeatureIcons` and `FileThumb` are the wrong size and the wrong job; there is no check-icon primitive.

Hero section keeps `aria-label="About Channeled"`.

Inside it, in order:

1. Existing brand row and `ThemeToggle`.
2. Title block: `h1` + lede.
3. Preview cluster, `aria-hidden="true"`.
4. The three existing point labels.

Point labels, in this order, unchanged:

- Independent teams and channels
- Private, self-hosted storage
- A full audit trail

Replace the disc with a check. Inline SVG, `aria-hidden="true"`, `viewBox="0 0 24 24"`, no fill, `stroke="currentColor"`, stroke width 1.7, round caps and joins, path `M5 12l5 5 9-10`. Box 18×18.

Preview cluster, back to front:

1. Back plate. Absolute. `left: 60px; top: 0; width: 300px; height: 190px; border-radius: 16px; background: #B7D9DF; transform: rotate(-6deg)`. `#B7D9DF` is not a token. Use this literal.
2. File card. Absolute. `left: 20px; top: 40px; width: 300px; padding: 16px; border-radius: 16px; background: var(--color-surface); color: var(--color-text); box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35); transform: rotate(-2deg)`.
   - Bold line: `# brand-assets`
   - Rows, 10px gap, each a 36×36 badge (radius 10px, 10px, weight 800) plus a bold filename:
     - `PDF` on `#F6E4E0` / `#8E3F33`, filename `guidelines-v3.pdf`
     - `PNG` on `#E4EBF3` / `#3E5A78`, filename `campaign-hero.png`
     - `SVG` on `#E1EEF0` / `#373f51`, filename `logo-primary.svg`
3. Activity card. Absolute. `left: 160px; top: 190px; width: 300px; padding: 12px 16px; border-radius: 16px; background: var(--color-surface); color: var(--color-text); box-shadow: 0 20px 40px rgba(0, 0, 0, 0.35)`. Row: 28×28 circle, background `--color-accent`, text `--color-accent-contrast`, 11px weight 700, initials `OS`; then “Omar S. uploaded 2 files” with the name bold.

These badge colors and the back-plate color are frame literals. Do not add tokens for them. Do not reuse `--shadow-card`; that shadow is a different value.

## CSS in `LoginPage.module.css`

`.hero`:

- `background: var(--color-accent)`
- `color: var(--color-accent-contrast)`
- remove both radial gradients and `--gradient-teal-soft`
- remove `.hero::after`
- `gap: 0` so the title block’s 56px top margin and the points’ auto margin match the frame
- keep `flex: 1 1 520px`, `padding: 56px 64px`, column flex, `overflow: hidden`, and `min-height: 100vh` / `100dvh`

`.heroTitle`: set `max-width: 500px`. Keep weight 700, `font-size: clamp(1.75rem, 4vw, 2.625rem)` (42px at the top of the clamp), line-height 1.15 is close enough to 1.1 to leave, and the existing `0 0 14px` margin. Color inherits.

`.heroLede`: `color: inherit`. Keep 16px and `max-width: 460px`.

Title block: `margin-top: 56px`.

Preview cluster: `position: relative; height: 300px; margin: 24px 0; max-width: 460px`.

`.heroPoints`: keep `margin-top: auto`, column, `gap: 10px`.

`.heroPoints p`: `color: inherit`. Delete the `::before` disc.

`.logo`: keep size, radius, and `--color-accent-strong`. Set the glyph to `color: var(--color-on-accent-strong)`.

Leave the `@media (max-width: 900px)` hero padding (`32px 24px`) and `min-height: auto`. The cluster may clip through `overflow: hidden`. Do not add a new breakpoint.

## Out of scope

- Landing page and `web/src/features/home/HomePage.tsx`
- Form card, fields, button, hints, and footers
- Signup vs login copy
- “Show”, “Keep me signed in”, and “Forgot password?”
- `Button` primary gradient
- Reset, invite, share, join, and email-confirmation pages
- New global tokens or `ui/` components

## Done when

- `/login` and `/signup` heroes are solid `--color-accent` with `--color-accent-contrast` type, check marks, and the two cards.
- Login and signup headlines and ledes are unchanged.
- The form column is unchanged.
- Light and dark both use the tokens above; the theme toggle still sits in the hero.
- `web/e2e/a11y-themes.spec.ts` still covers `/login` and `/signup`. Decorative cards and check icons stay `aria-hidden`. No login/signup screenshot baseline exists in `web/e2e/visual.spec.ts`.
