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


## Composable CI configuration: the consumer chooses the behavior

The consumer-facing experience should be copy/paste configuration, not a fork of the generator and not a mandatory all-in-one UML mode. A repository owner copies a small, versioned CI entry from us, replaces repository-specific values, and opts into additional capabilities by adding configuration. The configuration is the composition contract: the same structural engine can serve many repositories, while each CI pipeline selects its own stages.

The following is a **proposed consumer-facing shape**, not a claim that a reusable workflow with this interface has already shipped. Keep the first public contract intentionally small and versioned.

```yaml
# .github/uml.yml — proposed capability composition
schemaVersion: 1

source:
  repository: owner/repository
  revision: ${{ github.sha }}
  projects:
    - src/MyLibrary/MyLibrary.csproj

capabilities:
  - id: uml.structure
    version: "1"
    enabled: true

  # Optional: omit this whole entry when only structural UML is wanted.
  - id: uml.use-cases
    version: "1"
    enabled: true
    provider:
      capability: headless-ai.inference
      endpoint: ${{ vars.UML_LLM_ENDPOINT }}
      model: ${{ vars.UML_LLM_MODEL }}
      credential: ${{ secrets.UML_LLM_CREDENTIAL }}
    inputs:
      include:
        - public-api
        - source-grounded-responsibilities
        - tests
    outputs:
      - use-cases
      - alternate-flows
      - error-flows
      - candidate-sequence-diagrams

outputs:
  directory: artifacts/uml
  preserve-authored-files: true
  require-provenance: true
```

The exact field names and reusable-workflow invocation must be validated before publication. Credentials belong in CI secrets, non-secret configuration in repository variables/configuration, and endpoint/model choice remains consumer-owned. Never put credentials in a committed configuration string or generated artifact.

The copy/paste CI entry should be thin and pinned to an immutable release or commit. It should pass configuration to the versioned UML capability runner rather than embed generation logic in every consuming repository. Repository-specific values (project paths, target frameworks, output policy, optional provider configuration) are substitutions, not forks.

### Composition rules

- **Structure is the baseline capability.** It works without a model, API key, or network access to an inference provider.
- **Use cases are an independent opt-in capability.** Adding the use-case entry on a later commit activates it on the next CI run; it must not require a structural-generator redesign.
- **Capabilities declare inputs, outputs, version, and policy needs.** The runner validates configuration and dependency compatibility before execution.
- **AI configuration selects a provider contract, not arbitrary authority.** The provider receives bounded evidence and returns claims; it cannot commit to the repository, approve its own output, or run commands in the consumer environment.
- **Each capability has separate output ownership.** For example, structural pages and AI-proposed use cases have distinct paths/manifests so enabling one does not overwrite the other's files or human-authored documentation.
- **The pipeline composes capabilities, not diagram types hard-coded into the engine.** Future sequence, lifecycle, or workflow generation should be additional entries using the same contract.
- **Pin executable dependencies.** Pin the reusable workflow/runner and source revision; record tool, schema, model/provider configuration, and accepted artifact provenance. Mutable tags alone are not a sufficient provenance record.
- **Fail closed on invalid composition.** Unknown capability IDs/versions, missing required settings, unauthorized endpoints, invalid output schemas, or conflicting output ownership produce actionable diagnostics rather than silently changing behavior.

This changes the acceptance test from “the generator can produce use cases” to two separate proofs: (1) structural-only configuration generates valid structural UML without AI dependencies; (2) adding the optional use-case configuration makes the next CI run generate bounded, provenance-bearing use-case proposals while leaving structural and authored outputs intact.


## Implementation handoff and current blocker

The first cross-repository implementation task is tracked in [GUI issue #46: Expose source-derived UML as a consumer-configurable capability](https://github.com/TrentBest/TheSingularityWorkshop.GUI/issues/46). This is coordination only; the GUI source and workflows have not been changed by CiAi.

The current GUI workflow invokes `samples/SourceUmlGenerator` with GUI-owned project paths and project references to GUI Core and GUI.SVG. It already supports project-semantic models and provenance, but it is not yet a generic external-consumer entry point. The correct next step is to extract or expose the smallest renderer-neutral structural capability and prove it against a non-GUI project. Do not copy the GUI generator into CiAi, and do not advertise the proposed YAML as executable until that proof and a pinned reusable entry point exist.

The first proof should use pinned CogA source and a pinned generator revision. It must produce a deterministic structural artifact and provenance manifest without invoking an LLM. AI semantic enrichment should be added only as a separate, optional stage after the structural boundary is stable. If the generator cannot yet accept an external project without GUI references, record that as the blocker rather than faking a successful CogA generation.
