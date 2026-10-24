# Cog Ecosystem Integration Contract Tests

**Purpose:** prove that the five standalone CiAi cogs can participate in one bounded request-to-result flow together with the parent CiAi artifact contracts.

This is a local, deterministic contract proof. It does not contact a provider, authenticate a remote service, or claim end-to-end network orchestration.

Run in a workspace containing sibling checkouts named `CiAi`, `CogA`, `CogB`, `CogC`, `CogD`, and `CogE`:

```bash
dotnet run --project CiAi/integration/CogEcosystem.ContractTests --configuration Release
```

The GitHub Actions workflow pins each Cog checkout to a full commit SHA and executes the same integration harness.
