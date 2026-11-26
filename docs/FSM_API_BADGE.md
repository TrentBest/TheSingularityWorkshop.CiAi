# Powered by FSM_API badge

The badge is a visual projection of two independent facts:

1. **Dependency provenance** — whether an exact consumer build proves that FSM_API was resolved.
2. **Latest build outcome** — whether the latest bound consumer workflow attempt succeeded, failed, is in progress, or is unknown.

The SVG template at `Documentation/Branding/fsm-api-powered.svg` defaults to `state-unknown`. It must never be changed to a success state merely because the repository declares a PackageReference or because some unrelated build passed.

## Visual states

The renderer selects exactly one root CSS class:

| Root class | Meaning | Visual behavior |
|---|---|---|
| `state-verified-success` | Exact current evidence proves FSM_API consumption and the bound latest build succeeded | Colorful interlocking gears rotate |
| `state-build-failed` | Exact current evidence proves FSM_API consumption, and the bound latest build failed | Gears stay stationary; steam puffs appear |
| `state-build-in-progress` | A bound consumer build is still running | Gears remain stationary; label says BUILDING |
| `state-stale` | Historical dependency evidence exists but is not current | Stationary gears; STALE label |
| `state-unknown` | Evidence is missing, incomplete, malformed, conflicting, or unavailable | Stationary gears; UNKNOWN label |
| `state-not-detected` | A complete successful inspection found no supported FSM_API dependency | Stationary gears; NOT DETECTED label |

A build failure does not erase a previously verified dependency identity. A successful build does not prove FSM_API consumption. The renderer must keep those dimensions separate and choose the failure animation only when failure itself is evidenced. In-progress, stale, unknown, and not-detected states never animate as success.

## Accessibility and fallback

- The SVG includes a title and description, plus an accessible README alt label.
- `prefers-reduced-motion: reduce` disables rotation and steam movement; failure retains a static steam cue.
- The neutral static state remains useful when a host strips SVG animation or CSS.
- Animation is decorative. The future README target and evidence report—not the SVG itself—must carry the authoritative state and provenance.

## Renderer boundary

The generic `CiAiBadgeDefinition` declares the provider, renderer, output path, alt text, evidence source, and explicit unknown state. The badge definition does not fetch evidence or mutate repository files.

The evidence adapter must reuse CiAi's existing exact workflow/run/attempt/checkout provenance. It must consume actual resolved dependency data from the build (for example, normalized `project.assets.json` or lock-file data), not a project-file declaration. Its result must be bound to the exact consumer repository, commit, workflow run, and attempt before the renderer selects a state.

GitHub README rendering and reduced-motion behavior still require visual verification on the draft PR. No verified consumer badge is claimed yet.
