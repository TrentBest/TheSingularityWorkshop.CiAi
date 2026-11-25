# Repository manifest v1

CiAi repositories may declare their repository-level composition in
`.ciai/repository.json`. The manifest is data, not executable workflow
instructions. It is parsed into the existing immutable
`CiAiRepositoryDefinition` builder model.

## Supported fields

- `schema`: must be `ciai.repository.v1`.
- `repository`: canonical `owner/repository` identity.
- `observerProfile`: optional built-in profile ID (`ciai-default`,
  `rawwar-contracts-v1`, or `anyapp-development-v1`).
- `badges`: live workflow badge declarations.
- `dependencies`: direct repository dependencies with optional capability
  labels.
- `extensions`: declarative extension IDs and versions.

Unknown properties, duplicate JSON properties, unknown observer profiles,
invalid identities, and self-dependencies are rejected. The parser does not
execute extension content or infer package adoption.

## Version boundary

Manifest v1 intentionally serializes direct repository dependencies only.
The fluent builder can also express ordered fallback dependencies and ADP
pipeline/allocation declarations, but those richer declarations are not
silently flattened into this schema. `ToJson()` rejects a definition that
contains dependency expressions v1 cannot represent. A later schema revision
must encode those concepts explicitly and preserve their order and bounds.

## Observer profiles

Profiles describe expected workflow paths, artifact naming templates, and
test-result entry names. They are not evidence. Provider-observed run/job/
artifact metadata, exact source and checkout identities, workflow-definition
trust, and host authorization remain independent inputs to any evidence
verdict.

The manifest is therefore a composition declaration—not a release gate, CI
dispatch permission, LLM budget grant, or publication authorization.

## Automated validation

The repository solution includes the read-only validator:

```bash
dotnet run --project tools/CiAi.Repository -- --check-manifest
```

The command verifies the manifest schema, repository identity (when
`GITHUB_REPOSITORY` is available), and canonical deterministic JSON. It does
not write files or contact GitHub. The main contract workflow runs it after
the solution build.

Consumers can call
`.github/workflows/ciai-repository.yml` as a reusable workflow. Pin the
workflow reference itself and its required `ciai-ref` input to the same
reviewed, immutable 40-character CiAi commit SHA. The reusable workflow checks
out the consumer's exact event commit, validates the pin, and runs the
read-only manifest check. No consumer workflow has been migrated by adding
this reusable capability.
