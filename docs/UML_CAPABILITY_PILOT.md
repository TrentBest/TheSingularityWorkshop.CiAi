# UML Capability Feasibility Pilot

**Status:** feasible in principle; existing producer is a strong prototype, but not yet a reusable capability package.  
**Coordinator:** CiAi.  
**Candidate implementation source:** [GUI source-derived UML tooling](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/docs/SOURCE_DERIVED_UML.md).  
**Scope:** prove that a pinned C# repository revision can be analyzed into source-grounded UML/documentation artifacts without treating generated output as authority or granting the provider write access to the consumer.

## Finding

The GUI repository already contains a meaningful source-derived pipeline, so we should not start from a blank UML repository or invent a second diagram engine.

The current pipeline uses Roslyn syntax analysis and MSBuild/Roslyn project-semantic analysis, builds a normalized renderer-neutral model, renders focused SVG through GUI.SVG, and generates per-type Markdown/SVG pages plus an inventory. It carries repository/revision/project provenance and source spans, fails closed when project compilation fails, validates generated coverage, and uses manifest-owned promotion to avoid overwriting hand-authored documentation.

The maintained GUI solution inventory is explicitly scoped to four product projects (Core, Blazor, WPF, SVG). Its current generated inventory reports 69 types: Core 31, Blazor 6, WPF 23, SVG 9. That demonstrates a real producer and a nontrivial output set, but it does not prove the tool can analyze arbitrary consumer repositories or the five CiAi Cogs without adaptation.

See the [source-derived UML implementation guide](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/docs/SOURCE_DERIVED_UML.md), [generator](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/samples/SourceUmlGenerator/Program.cs), [solution inventory driver](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/scripts/generate_solution_uml.py), and [safe promotion workflow](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/.github/workflows/promote-source-uml-docs.yml).

## What exists versus what is missing

| Capability | Evidence / current boundary |
|---|---|
| C# syntax-derived type cards | Exists in the GUI staging generator |
| Project-semantic relationships | Exists through Roslyn/MSBuild compilation and symbols |
| Normalized model with provenance and source spans | Exists in the GUI tool |
| Focused SVG diagrams and per-type Markdown pages | Exists |
| Solution inventory with explicit project scope | Exists, currently hard-coded to four GUI product projects |
| Exact-build reflection check | Exists, but documented parity is currently type-identity-level rather than complete member/API parity |
| Manifest-owned output promotion and drift checking | Exists in the GUI repository |
| Human-authored use cases | Must remain separate; source analysis cannot reliably infer user intent |
| Reusable, stable consumer-facing contract | **Missing**; tooling is currently a staging/sample implementation, not an installable capability |
| Generic multi-repository / multi-project input selection | **Missing**; current solution driver hard-codes the GUI repository and its project allowlist |
| CiAi request/notice/receipt round trip for UML artifacts | **Not implemented** |
| Authenticated provider retrieval, durable client staging, local acceptance | **Not implemented** |

## Smallest credible proof

Use CogA first, then repeat against CogB–CogE only after the first result is useful. Each input must be a full immutable commit SHA, not a moving branch name.

1. **Pin input:** record repository full name, source commit SHA, selected project file(s), target framework(s), and generator revision.
2. **Discover scope explicitly:** classify product/library projects separately from tests, samples, build tooling, and generated files. Fail closed if an unclassified project appears; do not silently skip it.
3. **Build and analyze:** compile the selected project in a clean CI job, then run Roslyn semantic analysis. Compilation or symbol-resolution failure is a diagnostic failure, not permission to fall back to invented relationships.
4. **Generate output:** produce a normalized model, inventory, focused type pages, and SVG diagrams. Preserve stable type/member identities, source spans, relationship kinds, source revision, generator revision, and unresolved/external states.
5. **Validate artifact:** check deterministic output for the same inputs, complete declared type/page/diagram coverage, valid SVG accessibility metadata, valid local links, and output hashes. Separate generated files from hand-authored use-case documentation.
6. **Package immutably:** emit an artifact manifest containing request ID, repository/source SHA, generator identity/revision, selected scope, contract version, file inventory, sizes, media types, and SHA-256 digests.
7. **Client pull proof:** CiAi retrieves the artifact through an explicitly allowed HTTPS destination, enforces size/redirect/authentication policy, stages bytes inside the client boundary, verifies digest and provenance, and records a durable idempotent receipt. It does not execute the artifact or automatically commit generated files.
8. **Review boundary:** generated source-structure docs may be proposed to the consumer repository, but human use cases and final acceptance remain consumer-owned.

## Acceptance criteria

The pilot succeeds only when all are true:

- [ ] Same pinned source + generator revisions produce byte-identical output, or every nondeterministic field is explicitly normalized and documented.
- [ ] Every included type has one inventory entry, Markdown page, and SVG; excluded projects/files are listed with a reason.
- [ ] Every asserted relationship is backed by compiler-symbol evidence or marked unresolved; no relationship is inferred solely from a matching name.
- [ ] Every source link points to the pinned revision and correct source span.
- [ ] A deliberate source mutation changes the appropriate model/output and is caught by drift checking.
- [ ] A malformed project, compilation failure, ambiguous target, or unexpected project causes a bounded diagnostic failure.
- [ ] A tampered artifact, mismatched source SHA, oversized payload, wrong artifact identity, or repeated conflicting idempotency key is rejected by client-side validation.
- [ ] Generated output cannot overwrite or prune hand-authored consumer files.
- [ ] CiAi's receipt means only what its fields attest to; it is not approval to merge, publish, execute, or delete provider data.

## Architectural recommendation

Keep CiAi as the coordinator and keep UML generation as a capability provider. Do not yet create a separate UML repository merely to hold a name or wrapper. First extract a small reusable CLI/library and contract from the GUI staging tool, preserving GUI.SVG as an optional renderer rather than coupling the normalized model to SVG.

The first implementation boundary should be:

`Pinned source + explicit project selection + pinned generator`
→ `normalized model + diagnostics + rendered files + manifest`
→ `immutable provider artifact`
→ `client-side pull, digest/provenance verification, local review`

A dedicated `TheSingularityWorkshop.UML` repository becomes justified when the extracted tool has a stable CLI/API, isolated tests, a reproducible package/artifact, and at least one successful consumer outside GUI. CogA is the first consumer candidate because its scope is small and its existing catalog contracts give us a concrete way to check whether generated docs accurately describe the implementation.

## Important limits

This pilot documents source structure, not full product intent. It cannot infer all business use cases, policy decisions, runtime behavior, or cross-repository compatibility from source files alone. Tests and repository declarations can add evidence, but they should be labeled separately from source-derived facts. A green generator workflow proves generation and validation for that exact input; it does not prove semantic correctness without review, nor does it authorize publication or consumer writes.
