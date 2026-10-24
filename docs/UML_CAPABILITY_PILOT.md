# UML Capability Feasibility Pilot

**Status:** feasible in principle; existing producer is a strong prototype, but not yet a reusable capability package.  
**Coordinator:** CiAi.  
**Candidate implementation source:** [GUI source-derived UML tooling](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/docs/SOURCE_DERIVED_UML.md).  
**Scope:** prove that a pinned C# repository revision can be analyzed into source-grounded UML/documentation artifacts, augmented by an explicit AI semantic layer, without treating generated output as authority or granting the provider write access to the consumer.

## Finding

The GUI repository already contains a meaningful source-derived pipeline, so we should not start from a blank UML repository or invent a second diagram engine.

The current pipeline uses Roslyn syntax analysis and MSBuild/Roslyn project-semantic analysis, builds a normalized renderer-neutral model, renders focused SVG through GUI.SVG, and generates per-type Markdown/SVG pages plus an inventory. It carries repository/revision/project provenance and source spans, fails closed when project compilation fails, validates generated coverage, and uses manifest-owned promotion to avoid overwriting hand-authored documentation.

The maintained GUI solution inventory is explicitly scoped to four product projects (Core, Blazor, WPF, SVG). Its current generated inventory reports 69 types: Core 31, Blazor 6, WPF 23, SVG 9. That demonstrates a real producer and a nontrivial output set, but it does not prove the tool can analyze arbitrary consumer repositories or the five CiAi Cogs without adaptation.

See the [source-derived UML implementation guide](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/docs/SOURCE_DERIVED_UML.md), [generator](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/samples/SourceUmlGenerator/Program.cs), [solution inventory driver](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/scripts/generate_solution_uml.py), and [safe promotion workflow](https://github.com/TrentBest/TheSingularityWorkshop.GUI/blob/development/.github/workflows/promote-source-uml-docs.yml).

## Two evidence layers: deterministic structure and AI semantics

The UML system should not expect a syntax/semantic analyzer to invent human use cases. It should consume the AI packages through explicit, versioned contracts and combine their output with source-derived evidence.

### Layer A — deterministic structural facts

Roslyn/MSBuild and repository tooling establish what can be verified from a pinned revision:

- Types, members, interfaces, inheritance, references, and compiler-resolved relationships.
- Project and package dependencies, public API surface, and test evidence.
- Source spans and links tied to the exact commit.
- Build diagnostics, coverage, unresolved symbols, and generator provenance.

These claims should be reproducible for the same source, toolchain, configuration, and generator revisions. An unresolved relationship stays unresolved; matching names alone are not proof.

### Layer B — AI-produced semantic interpretation

The AI layer proposes the information that source structure alone cannot reliably determine:

- Intended responsibilities and plain-language explanations.
- Actor-centered and system-level use cases, alternate paths, error paths, and edge cases.
- Cross-component workflows and candidate sequence/activity diagrams.
- Potential design gaps, missing tests, ambiguity, and questions for a human reviewer.
- Candidate links between implementation facts and the repository's declared product intent.

Treat these as hypotheses, not compiler facts. Every claim should carry a stable claim ID, evidence references (source spans, tests, docs, or explicitly stated assumptions), confidence/rationale where supported, and a provenance record identifying the AI capability/version and generation configuration. The output should label claims **source-verified**, **test-evidenced**, **AI-inferred**, or **human-approved**. Human-authored intent must not be silently replaced by generated prose.

The LLM's reasoning may be nondeterministic. Therefore, reproducibility applies to the deterministic structural layer; AI semantic outputs must instead be versioned, captured as immutable artifacts, and reviewable. If a stable published document is required, pin the accepted AI result as an artifact rather than pretending a fresh inference will always reproduce the same wording.

### AI package responsibilities and boundaries

- **HeadlessAi** is a provider-neutral path for invoking configured AI-capable HTTP endpoints. The host retains authority over task intent, permissions, output validation, memory, and actions.
- **ProtocolAi** supplies deterministic application-owned semantic identities and vocabulary resolution. It is not an inference engine or transport.
- **GrammarAi** supplies deterministic structure/production rules that connect protocol identities. It does not invoke a model or execute workflows.
- **CiAi** coordinates capability discovery, policy, requests, result correlation, artifact handoff, and receipts.

UML should use HeadlessAi for an explicitly configured inference call, ProtocolAi to give domain terms stable identities, and GrammarAi to describe permitted relationships between those identities—where those contracts genuinely fit. It must not add provider-specific SDK coupling, bypass host policy, or assume that ProtocolAi/GrammarAi themselves perform inference. The UML core and structural model should remain usable without a live LLM; AI enrichment should be an explicit capability/pipeline stage with bounded inputs and outputs.

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
| AI-assisted use-case and edge-case discovery | **Architectural requirement; not yet integrated into the UML producer** |
| Explicit HeadlessAi / ProtocolAi / GrammarAi contracts in UML | **Not yet implemented; integrate through versioned interfaces and host-owned policy** |
| Claim-level evidence/provenance and review status for AI output | **Not yet implemented** |
| Reusable, stable consumer-facing contract | **Missing**; tooling is currently a staging/sample implementation, not an installable capability |
| Generic multi-repository / multi-project input selection | **Missing**; current solution driver hard-codes the GUI repository and its project allowlist |
| CiAi request/notice/receipt round trip for UML artifacts | **Not implemented** |
| Authenticated provider retrieval, durable client staging, local acceptance | **Not implemented** |

## Smallest credible proof

Use CogA first, then repeat against CogB–CogE only after the first result is useful. Each input must be a full immutable commit SHA, not a moving branch name.

1. **Pin input:** record repository full name, source commit SHA, selected project file(s), target framework(s), and generator revision.
2. **Discover scope explicitly:** classify product/library projects separately from tests, samples, build tooling, and generated files. Fail closed if an unclassified project appears; do not silently skip it.
3. **Build and analyze:** compile the selected project in a clean CI job, then run Roslyn semantic analysis. Compilation or symbol-resolution failure is a diagnostic failure, not permission to fall back to invented relationships.
4. **Generate structural output:** produce a normalized model, inventory, focused type pages, and SVG diagrams. Preserve stable type/member identities, source spans, relationship kinds, source revision, generator revision, and unresolved/external states.
5. **Enrich semantically:** send a bounded, explicitly selected evidence package to the configured AI capability. Request use cases, alternate/error paths, candidate workflows, and open questions as structured claims referencing evidence IDs. Do not ask the LLM to restate compiler facts without their evidence, and do not let it write directly into authored docs.
6. **Validate both layers:** deterministic structural output must be reproducible. AI output must pass schema, size, reference-integrity, and policy checks; unsupported claims are flagged, not promoted to facts. Preserve model/capability version and the exact accepted output.
7. **Validate artifact:** check declared type/page/diagram coverage, valid SVG accessibility metadata, valid local links, and output hashes. Keep generated structural files, AI proposals, and hand-authored use-case documentation in distinct ownership areas.
8. **Package immutably:** emit an artifact manifest containing request ID, repository/source SHA, generator identity/revision, selected scope, contract version, AI capability/configuration provenance when used, file inventory, sizes, media types, and SHA-256 digests.
9. **Client pull proof:** CiAi retrieves the artifact through an explicitly allowed HTTPS destination, enforces size/redirect/authentication policy, stages bytes inside the client boundary, verifies digest and provenance, and records a durable idempotent receipt. It does not execute the artifact or automatically commit generated files.
10. **Review boundary:** generated source-structure docs may be proposed to the consumer repository. AI use cases remain explicitly inferred until human-approved; acceptance remains consumer-owned.

## Acceptance criteria

The pilot succeeds only when all are true:

- [ ] Same pinned source + generator revisions produce byte-identical structural output, or every nondeterministic field is explicitly normalized and documented.
- [ ] Every included type has one inventory entry, Markdown page, and SVG; excluded projects/files are listed with a reason.
- [ ] Every asserted structural relationship is backed by compiler-symbol evidence or marked unresolved; no relationship is inferred solely from a matching name.
- [ ] AI use-case claims reference evidence IDs or clearly state their assumptions; claim status distinguishes inference from verified facts.
- [ ] AI output is schema-validated, bounded, versioned, and reviewable; an unavailable model does not prevent structural UML generation.
- [ ] Every source link points to the pinned revision and correct source span.
- [ ] A deliberate source mutation changes the appropriate model/output and is caught by drift checking.
- [ ] A malformed project, compilation failure, ambiguous target, unexpected project, or invalid AI response causes a bounded diagnostic result.
- [ ] A tampered artifact, mismatched source SHA, oversized payload, wrong artifact identity, or repeated conflicting idempotency key is rejected by client-side validation.
- [ ] Generated output cannot overwrite or prune hand-authored consumer files.
- [ ] CiAi's receipt means only what its fields attest to; it is not approval to merge, publish, execute, or delete provider data.

## Architectural recommendation

Keep CiAi as the coordinator and keep UML generation as a capability provider. Do not yet create a separate UML repository merely to hold a name or wrapper. First extract a small reusable CLI/library and contract from the GUI staging tool, preserving GUI.SVG as an optional renderer rather than coupling the normalized model to SVG. Do not modify the GUI-owned implementation without coordination; use it as the reference prototype.

The first implementation boundary should be:

`Pinned source + explicit project selection + pinned structural generator`
→ `normalized model + diagnostics + structural diagrams + manifest`
→ `optional AI semantic enrichment via HeadlessAi / ProtocolAi / GrammarAi contracts`
→ `immutable provider artifact`
→ `client-side pull, digest/provenance verification, local review`

A dedicated `TheSingularityWorkshop.UML` repository becomes justified when the extracted tool has a stable CLI/API, isolated tests, a reproducible package/artifact, and at least one successful consumer outside GUI. CogA is the first consumer candidate because its scope is small and its existing catalog contracts give us a concrete way to check whether generated docs accurately describe the implementation.

## Important limits

Structural UML documents source structure, not full product intent. AI can propose business use cases, runtime scenarios, and edge cases, but inference is not proof. Tests and repository declarations add separate evidence. A green generator workflow proves generation and validation for that exact input; it does not prove semantic correctness without review, nor does it authorize publication or consumer writes.
