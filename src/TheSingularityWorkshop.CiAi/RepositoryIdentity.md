# RepositoryIdentity

**User moniker:** Canonical Repository Identity Gate

## Purpose

`RepositoryIdentity` is the parent CiAi implementation's shared, internal rule for repository identity shape. Both the GitHub Actions request path and the resulting `CiRunSnapshot` use this validator, preventing those two trust boundaries from drifting apart.

## Rules

- Exactly two slash-separated components: owner and repository.
- Total identity length is at most 200 characters.
- Each component is 1–100 characters.
- Dot-only components `.` and `..` are rejected.
- Components accept ASCII letters, ASCII digits, hyphen, underscore, and period only.
- Blank identities and whitespace-containing identities are rejected by the character/shape rules.

## Trust boundary

This validates syntax, not whether the repository exists or whether the caller may access it. GitHub remains authoritative for repository identity and run facts; `GitHubActionsRunClient` validates the returned repository and run ID against the requested identity.

## Verification

The offline GitHub Actions client contract tests reject path traversal and overlong owner components before sending an HTTP request.
