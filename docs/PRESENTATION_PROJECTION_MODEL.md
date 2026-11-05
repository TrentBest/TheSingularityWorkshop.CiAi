# Presentation and Projection Boundaries

**Status: architecture proposal; no HTTP host or new renderer is implemented by this document.**

## Intent

A capability's data should be presentable in more than one place without making the underlying capability depend on a particular GUI framework. A Blazor application is one useful host, not the universal presentation contract. HTTP is worth treating as a first-class delivery boundary because browsers, articles, documentation systems, dashboards, and other applications can consume web resources.

Keep these concerns distinct:

- **Semantic model:** the authoritative, typed facts and relationships owned by the capability.
- **Projection:** a representation of those facts for a particular audience or medium.
- **Renderer:** turns a projection into visible or audible output.
- **Delivery/host adapter:** makes a projection available to a caller or embeds it in a host.
- **Interaction/command boundary:** accepts user intent separately from read-only presentation and applies policy before any side effect.

HTTP is a transport and hosting option, not itself a visual renderer. Blazor is a UI framework/host that can consume the same semantic model or projection.

## Useful presentation and delivery forms

| Form | Good fit | Important boundary |
|---|---|---|
| Plain text / Markdown | LLM context, README files, articles, chat, accessible summaries | Keep facts and evidence links traceable; do not treat prose as executable authority |
| HTML + CSS | Web pages, documentation, articles, dashboards, reusable web fragments | Sanitize untrusted content; respect host Content Security Policy and embedding restrictions |
| SVG | Diagrams, graphs, icons, scalable living documentation, article embeds | A rendered SVG proves the projection shown, not hidden runtime behavior or GPU execution |
| Canvas / WebGL / WebGPU | Dense, animated, interactive 2D/3D scenes | Keep GPU execution and performance claims tied to actual runtime evidence |
| Blazor | Interactive .NET web application and component UI | Keep it an adapter/host, not a dependency of provider-neutral core contracts |
| Static image / PDF | Reports, tickets, offline review, fixed snapshots | Snapshot only; show when and from what source the image was generated |
| Terminal / tables | CI logs, command-line workflows, diagnostics | Preserve status distinctions and links to authoritative evidence |
| HTTP API | Cross-application data access and integration | Version contracts, authenticate where needed, validate inputs, and separate read from mutation |
| Server-sent events / WebSockets | Live updates, progress feeds, collaborative or high-frequency interaction | Add only when polling or static snapshots are insufficient; define reconnect, ordering, stale-data, and authorization behavior |

These are options, not a mandate to implement every renderer. Choose the smallest adapter that serves a real consumer.

## HTTP projection options

HTTP could serve several different products; keep their responsibilities explicit:

1. **Read-only resource endpoint** — returns a stable, typed representation of a capability, status, graph, or evidence snapshot. It can be consumed by another application without adopting Blazor.
2. **HTML/SVG document endpoint** — returns a directly viewable page, graph, or diagram. It can be linked from an article or documentation page, or embedded where the host allows it.
3. **Embeddable web fragment** — a self-contained HTML/SVG view with documented sizing, accessibility, refresh, and origin behavior. Prefer a narrow, explicit embed contract over requiring the whole application.
4. **Live update endpoint** — only when a real use case needs it, layer a documented streaming mechanism over the same semantic model.
5. **Command endpoint** — a separate, later capability. Never turn a read-only presentation endpoint into an implicit dispatch, publish, deploy, or other mutation path.

An HTTP URL alone does not make content embeddable everywhere. Hosts may block frames or remote resources through Content Security Policy, cross-origin rules, authentication requirements, or sanitization. An iframe, direct SVG/image resource, reusable web component, and copied static snapshot have different trade-offs; document which mode is supported and test it in target hosts.

## Recommended dependency direction

`Capability core -> semantic model -> projection -> renderer/host adapter -> destination`

Examples of destinations include Blazor, a plain web page, an article embed, a GUI.SVG view, a terminal summary, or a saved report. The core should not reference those presentation packages. GUI.SVG remains an optional visual/documentation surface. Renderer remains independent of Visualize and GUI.SVG; Visualize can manifest runtime-owned data into an appropriate projection. SVG is a useful proof of visible output, but it is not evidence of GPU execution.

For CiAi specifically, GitHub remains authoritative for CI facts. CiAi should normalize and correlate evidence; a presentation adapter may then expose the same verified model as readable text, an HTTP resource, or a graph. The UI must preserve distinctions such as success, failure, rejected, pending, stale, skipped, unavailable, and unknown rather than collapsing them into a color or a generic boolean.

## Security and evidence rules

- Start with read-only HTTP projections.
- Do not expose secrets, tokens, internal logs, or private repository facts through public embeds.
- Treat query parameters, requested repository identities, and any future commands as untrusted input.
- Keep authorization and mutation policy outside the renderer.
- Use restrictive embedding and content policies intentionally; do not enable arbitrary cross-origin framing by default.
- Include source identity, commit/run identity where applicable, observation time, and an evidence link when displaying CI state.
- Do not claim a live view is current unless freshness is known; label snapshots and stale/unavailable evidence honestly.
- Add caching, streaming, and persistence only when their consistency and invalidation semantics are specified.

## Suggested implementation sequence

1. Keep the current provider-neutral contracts and read-only CiAi observer as the source of facts.
2. Define one minimal, typed projection over verified evidence; do not invent a second serialization framework if an existing contract already fits.
3. Render that projection in the existing terminal/sample output first and test status edge cases.
4. Add a read-only HTTP endpoint that returns either the typed resource or a directly viewable HTML/SVG page, depending on the first concrete consumer.
5. Embed it in one real article/documentation page and test accessibility, sizing, cross-origin policy, refresh/freshness, and failure states.
6. Only then decide whether live streaming or a richer Blazor dashboard solves a demonstrated need.

## Acceptance criteria for a first HTTP proof

- No existing CI contract or provider boundary is replaced.
- GET-only projection is demonstrably read-only.
- Output is derived from validated source data, not model-generated claims.
- Unknown, missing, failed, stale, and unavailable evidence remain distinct.
- The page or resource can be consumed by a documented external host.
- Embed/security behavior and freshness are explicit.
- Tests cover malformed inputs, authorization boundaries, and empty/error states.
- The feature is described as an HTTP projection proof, not as a completed GUI or cross-repository integration.

## Decision

**Recommendation: yes to HTTP as a first-class delivery adapter; no to making HTTP a new monolithic GUI framework.** Start with one read-only projection and one real embedding use case. Retain Blazor for interactive .NET experiences, GUI.SVG for scalable visual proof/documentation, and renderer-neutral contracts as the common seam.
