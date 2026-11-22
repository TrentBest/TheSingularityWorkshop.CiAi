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
| SURPS target commit | `5086151e0724b3456572ea430561912e45ae3426` |
| CiAi observer source pin | `66af59177bbe67275915a4e11fd66ee05166fb6a` |
| AgentOnboarding contract | `ciai.agent-onboarding.v1` — **ELIGIBLE** |
| Onboarding content SHA-256 | `bab414bb994cb4b45198b614e8e7f195780b28aaad25d3dce151f7bca0bd2c0e` |
| CiAi evidence workflow | [run #45065198390](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/actions/runs/45065198390) — succeeded |
| Authoritative Pages workflow | [run #45065198365](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/actions/runs/45065198365) — terminal, failed |

The CiAi report marked discovery complete, required evidence complete, and the Pages workflow terminal. It correctly marked required workflow success false. The Pages build/upload job passed; the deployment job failed. The deployment root cause remains unresolved, so the hosted site is not yet considered verified.

The CiAi observer workflow's own success means it built the pinned observer, evaluated the exact commit's onboarding document, generated and schema-checked the evidence report, and uploaded the report. It does **not** mean the Pages deployment succeeded.

## Evidence workflow

SURPS currently declares:
- `.github/workflows/pages.yml` — static documentation build and deployment.
- `.github/workflows/ciai-evidence.yml` — read-only exact-commit observation and AgentOnboarding v1 admission.

The CiAi workflow pins its observer source to an immutable commit. It checks out the exact SURPS target SHA before passing the onboarding text to CiAi's actual `AgentOnboardingValidator.Evaluate` implementation.

The required workflow selector includes the exact Pages run ID and attempt. CiAi preserves separate facts for discovery completeness, evidence completeness, terminality, and success. A failed deployment remains failed evidence; it is not hidden by the observer workflow's successful report generation.

## Next integration steps

1. Diagnose the Pages deployment failure using the GitHub Pages environment/deployment evidence; do not weaken the evidence policy to make the report green.
2. Add a source-validation workflow appropriate to the documentation-first SURPS repository (link integrity, Markdown/front-matter checks, and static-site validation).
3. Include that workflow as a separate required evidence identity only after its purpose and execution are verified.
4. Extend the SURPS ontology and source-book work only from domain requirements; do not let CI policy dictate in-world schemas.
5. Keep CiAi read-only. Dispatch, repository writes, merge, release, package publication, and deployment authorization remain separate host/human gates.

## Coordination

- [SURPS onboarding issue #1](https://github.com/TrentBest/TheSingularityWorkshop.SingularityUniversalRoleplayingSystem/issues/1)
- [CiAi onboarding issue #84](https://github.com/TrentBest/TheSingularityWorkshop.CiAi/issues/84)

