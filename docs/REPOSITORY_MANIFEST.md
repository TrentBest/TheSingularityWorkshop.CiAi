# Repository manifest

CiAi repositories declare repository-level composition in `.ciai/repository.json`. The manifest is data, not executable workflow instructions. It is parsed into the existing immutable `CiAiRepositoryDefinition` builder model.

## Single prerelease contract

The current prerelease contract uses the existing identifier `ciai.repository.v1`. There is one canonical parser and serializer, with no compatibility split for unreleased formats.

Supported root fields:

- `schema`: exactly `ciai.repository.v1`.
- `repository`: canonical `owner/repository` identity.
- `observerProfile`: optional built-in profile ID.
- `badges`: provider-neutral declarative badge registrations, including renderer, artifact, accessibility, and evidence metadata.
- `dependencies`: direct repository dependencies with optional capability labels.
- `extensions`: declarative extension IDs and versions.
- `relationships`: directed repository relationship declarations.
- `adp`: optional bounded ADP settings; null or omission leaves autonomous development disabled.

Relationships are versioned edges with explicit source, target, artifact kind, and authority repository. Supported kinds are `ContentContribution`, `CanonicalContentConsumption`, `ExperienceValidation`, and `BuildDependency`.

The optional `adp` object contains `maximumRepairAttempts` and `requireHumanApprovalForWrites`. Relationship metadata does not itself enable autonomous execution. ADP is enabled only by explicit non-null settings.

Unknown properties, duplicate JSON properties, unknown observer profiles, invalid identities, self-dependencies, malformed relationships, and invalid ADP bounds are rejected. The parser does not execute extension content or infer package adoption.

Schema: [ciai.repository.schema.json](../schemas/ciai.repository.schema.json).

## Boundaries

The manifest is a composition declaration—not a release gate, CI dispatch permission, LLM budget grant, or publication authorization. It does not silently flatten dependency expressions it cannot represent; serialization rejects unsupported declarations rather than losing information.

Observer profiles describe expected workflow paths, artifact naming templates, and test-result entry names. They are not evidence. Provider-observed run/job/artifact metadata, exact source and checkout identities, workflow-definition trust, and host authorization remain independent inputs to any evidence verdict.

## Automated validation

```bash
dotnet run --project tools/CiAi.Repository -- --check-manifest
```

The command verifies the manifest schema, repository identity (when `GITHUB_REPOSITORY` is available), and canonical deterministic JSON. It does not write files or contact GitHub.

Consumers can call `.github/workflows/ciai-repository.yml` as a reusable workflow. Pin the workflow reference itself and its required `ciai-ref` input to the same reviewed, immutable 40-character CiAi commit SHA. No consumer workflow is migrated merely by adding this reusable capability.
