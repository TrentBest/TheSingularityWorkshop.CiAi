# Repository development progress v1

CiAi can facilitate a consistent, explainable development-progress badge for any
repository. The first contract is deliberately separate from
`.ciai/repository.json`: composition describes what a repository is connected
to; progress describes how its declared work is advancing.

## Design boundary

CiAi does **not** decide that a feature is complete, infer completion from
commit counts, count files, inspect arbitrary code and award points, or let an
LLM choose a flattering percentage. The repository's maintainers define the
workstreams, their relative weights, and the completion assessment. They attach
evidence references so a human or a future evidence-aware review can inspect
the reasoning.

CiAi facilitates by validating the declaration, calculating the deterministic
weighted projection when all workstreams have reviewed scores, and exposing a
stable contract that a renderer can consume. SVG presentation belongs in the
GUI.SVG layer; CiAi should not grow a second SVG rendering system.

## File

Each participating repository may add `.ciai/progress.json`.

- `schema`: exactly `ciai.progress.v1`.
- `repository`: canonical `owner/repository`, checked against
  `GITHUB_REPOSITORY` by CI when available.
- `title`: short human-readable scope label.
- `updatedUtc`: explicit UTC timestamp for the assessment.
- `method`: exactly `weighted-workstreams-v1`.
- `workstreams`: stable IDs, labels, integer weights, optional completion
  estimates, assessment status, and evidence references.

Weights must be positive integers and total exactly 100. IDs must be unique.
A completion score is either null or an integer from 0 through 100.
Assessment status is `not-assessed`, `estimated`, or `reviewed`.
A reviewed workstream must have a score and at least one evidence reference.
An unassessed workstream must have a null score. Evidence references are
pointers for review, not cryptographic proof or an automatic trust grant.

## Calculation

For a fully assessed profile:

`overall = sum(weight × completionPercent) / 100`

The unrounded value is retained in the machine-readable result; presentation
may round to the nearest whole percent. Workstream weights express relative
importance, not time spent. Reassess weights when scope changes and record the
reason in the repository's progress notes.

If any workstream is unassessed, the overall score is **unavailable**, not zero.
A badge must display an explicit unavailable/assessment-needed state rather
than silently omit unfinished areas or normalize the remaining weights.
The SVG should show the overall value and each workstream so the number is
explainable at a glance.

## Evidence and honesty rules

- A score measures declared scope completion, not code coverage, file count,
  issue count, elapsed time, or release probability.
- Evidence references should identify concrete artifacts: issue/PR, source
  commit, test run, benchmark, documentation, or owner-reviewed decision.
- A green CI run proves only the checks that actually ran for its exact commit.
  It does not prove every workstream is complete.
- Missing, stale, skipped, failed, or mismatched evidence must never increase
  completion.
- 100% means the declared scope has been assessed as complete and its listed
  evidence is ready for owner review. It is not release authorization.
- Keep the last assessment timestamp visible. Do not imply live status when
  the assessment is stale.

## Renderer boundary

The stable projection should be passed to GUI.SVG (or its agreed SVG contract)
for rendering. The renderer owns visual composition, accessible SVG title and
description, theme, dimensions, bars, and typography. CiAi owns the schema,
validation, weighted arithmetic, and evidence-reference semantics. A renderer
must not recalculate or reinterpret the score.

## Adoption path

1. Add `.ciai/progress.json` and agree the workstreams with the repository
   owner.
2. Run the CiAi progress validator in CI.
3. Review the workstream scores and evidence references; leave incomplete
   areas explicitly unassessed.
4. Connect the validated projection to the GUI.SVG renderer.
5. Add a README badge linking to the repository's progress/readiness detail.
6. Adopt the reusable workflow only after the repository has pinned the reviewed
   immutable CiAi revision.

The CiAi repository's initial progress file is intentionally unassessed. It
proves the shape and weighting contract without inventing a score for CiAi
itself.
