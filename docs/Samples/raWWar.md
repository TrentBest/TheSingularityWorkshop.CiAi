# raWWar: bidirectional SURPS producer/consumer integration

## Repository roles

[raWWar](https://github.com/TrentBest/raWWar) is both a producer and a consumer of SURPS content, and it owns the Experience it delivers.

- raWWar authors proposed roleplaying content.
- [SURPS](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem) admits, canonicalizes, versions, and owns shared roleplaying semantics.
- raWWar consumes the exact canonical SURPS content revision and validates compatibility with its own Experience.
- raWWar remains the authority for Experience composition, runtime behavior, and Experience acceptance.

CiAi coordinates the engineering pipeline. It does not become the authority for SURPS ontology or raWWar's Experience.

## Enrolled directed relationships

CiAi's `.ciai/repository.json` uses `ciai.repository.v2` and declares three distinct edges:

| ID | Direction | Kind | Authority |
|---|---|---|---|
| `rawwar-content-contribution` | raWWar → SURPS | ContentContribution | SURPS |
| `surps-canonical-content-consumption` | SURPS → raWWar | CanonicalContentConsumption | SURPS |
| `rawwar-experience-validation` | SURPS → raWWar | ExperienceValidation | raWWar |

These are content/Experience relationships, not inferred NuGet dependencies. The graph is enrolled with `adp: null`; durable event handling and autonomous execution are not yet enabled.

## Exact-source evidence profile

CiAi's built-in `rawwar-contracts-v1` observer profile identifies:

- `.github/workflows/build.yml`;
- provenance artifact `rawwar-workflow-provenance-{runId}-{attempt}`;
- contract artifact `rawwar-contract-results-{runId}-{attempt}`;
- provenance entry `workflow-provenance.json`;
- TRX entry `results.trx`;
- required job key/name `build-and-contracts`.

The pinned observer retrieves provider artifacts by exact run and attempt, verifies provider metadata and archive digests, parses workflow provenance, validates genuine TRX records, and compares workflow bytes at the run revision with a pinned approved baseline revision.

## First successful read-only proof

| Evidence | Result |
|---|---|
| raWWar source commit | `eef1974a87d78eefe2802d77d7f2451f4f2b47a7` |
| Source build | [45549019401](https://github.com/TrentBest/raWWar/actions/runs/45549019401) — success |
| Actual contract results | 406 passed / 406 total |
| CiAi observer | [45549019399](https://github.com/TrentBest/raWWar/actions/runs/45549019399) — success |
| Exact source identity | `.github/workflows/build.yml#45549019401#1` |
| Required workflow result | passed |
| Workflow definition | matched pinned approved baseline |
| TRX verdict | `CompletePass` |

This is a read-only evidence reconciliation proof. It is not proof that the Experience is complete or launchable, and it does not authorize repository mutation.

## Feedback and loop control

A contribution, admission, canonical publication, or Experience validation is a distinct event. Every event retains the exact source commit and artifact digest. A delivery retry with the same relationship/version, source revision, artifact identity, and digest deduplicates; changed source bytes create new validation work. Causation IDs connect feedback events but do not suppress changed work.

The loop is not an unbounded execution cycle. Each step is an immutable, bounded task with an explicit terminal result. Failed admission must not publish canonical content; canonical content changes trigger consumer/Experience revalidation; a new raWWar contribution is a new proposal against a new source revision.

## Remaining implementation

1. Persist relationship change notices and evidence receipts.
2. Map accepted events into bounded ADP work groups and existing Await/Gate contracts.
3. Connect work groups to FSM_Execution and the host authorization boundary.
4. Demonstrate controlled failure, diagnosis, bounded proposal, explicit authorization, isolated change, and new exact-SHA validation.

The observer workflow itself remains candidate-controlled repository configuration. The trusted host must independently validate its pinned observer revision and retain the trust decision. Merge, publication, release, and deployment remain separate authorization gates.
