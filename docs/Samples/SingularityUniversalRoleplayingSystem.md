# SingularityUniversalRoleplayingSystem: CiAi Integration

## Repository role

[TheSingularityWorkshop.SingularityUniversalRoleplayingSystem (SURPS)](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem) owns the shared roleplaying ontology: worlds, environments, entities, attributes, capabilities, behavior, agency, interactions, and narrative state.

SURPS is not a replacement for its supporting repositories:
- FSM_Dungeon owns the underground environment and its domain-specific phenomena.
- Physics owns physical laws and physical consequences.
- FSM_API owns state and transition foundations.
- FSM_COS / FSM_Foundry own experience composition and runtime execution.
- StoryBuilder owns narrative construction.
- GUI / GUI.SVG own presentation and living illustrations.

**CiAi is an engineering-governance capability, not an in-world behavior provider.** It observes exact-commit CI evidence and validates onboarding contracts. It does not author inhabitant behavior, execute builds, grant repository permissions, or authorize merges/releases.

## Current onboarding evidence

The first read-only evidence lane is implemented on SURPS `development`.

| Evidence | Exact result |
|---|---|
| SURPS target commit | `4c135bc5b7109515e55bb1f1e3476a550d4da723` |
| CiAi observer source pin | `66af59177bbe67275915a4e11fd66ee05166fb6a` |
| AgentOnboarding contract | `ciai.agent-onboarding.v1` — **ELIGIBLE** |
| Onboarding content SHA-256 | `bab414bb994cb4b45198b614e8e7f195780b28aaad25d3dce151f7bca0bd2c0e` |
| CiAi evidence workflow | [run #45065589819](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/actions/runs/45065589819) — succeeded |
| Authoritative Pages workflow | [run #45552474421](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/actions/runs/45552474421) — terminal, successful |

The earlier failed deployment was traced to the repository's `github-pages` environment deployment-branch restriction. After `development` was allowed, the existing failed deploy job was retried without rebuilding the artifact. The build and deploy jobs both completed successfully; the deploy log found the existing `github-pages` artifact, created the deployment for exact source SHA `009949d23ec0b04fa155451a4f16c1062cda210e`, and GitHub reported `Reported success!`. Published site: https://trentbest.github.io/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/.

The CiAi observer workflow remains a separate read-only evidence lane. Its success means it built the pinned observer, evaluated the exact commit's onboarding document, generated and schema-checked the evidence report, and uploaded the observation report and admission result. The Pages deployment is independently authoritative and is now confirmed successful by the provider deployment log.

## Evidence workflow

SURPS currently declares:
- `.github/workflows/pages.yml` — static documentation build and deployment.
- `.github/workflows/ciai-evidence.yml` — read-only exact-commit observation and AgentOnboarding v1 admission.

The CiAi workflow pins its observer source to an immutable commit. It checks out the exact SURPS target SHA before passing the onboarding text to CiAi's actual `AgentOnboardingValidator.Evaluate` implementation.

The required workflow selector includes the exact Pages run ID and attempt. CiAi preserves separate facts for discovery completeness, evidence completeness, terminality, and success. A failed deployment remains failed evidence; it is not hidden by the observer workflow's successful report generation.

## Rulebook identity and cover validation\n\nSURPS is defining a descriptor-backed sourcebook identity contract. Canonical titles follow `Singularity Universal Roleplaying System - {Category} - LOD {N} - {Rulebook Title}`. Each book must have a machine-readable descriptor, a matching LOD badge, and a real cover image checked into the repository. SURPS owns this schema and its validator; CiAi remains read-only and observes exact-commit evidence from the validator workflow. It must not create or repair metadata, substitute missing covers, or authorize publication/deployment.\n\nTracking: [CiAi issue #85 — SURPS rulebook identity and cover validation](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/85).\n\n## Next integration steps

1. Keep the Pages deployment environment's allowed-branch policy aligned with the intended publishing branch; do not weaken CiAi's evidence policy to make a report green.
2. Add a source-validation workflow appropriate to the documentation-first SURPS repository (link integrity, Markdown/front-matter checks, and static-site validation).
3. Include that workflow as a separate required evidence identity only after its purpose and execution are verified.
4. Extend the SURPS ontology and source-book work only from domain requirements; do not let CI policy dictate in-world schemas.
5. Keep CiAi read-only. Dispatch, repository writes, merge, release, package publication, and deployment authorization remain separate host/human gates.

## Coordination

- [SURPS onboarding issue #1](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/issues/1)
- [CiAi onboarding issue #84](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/84)

