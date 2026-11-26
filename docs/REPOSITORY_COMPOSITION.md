# CiAi repository composition

## One declaration surface

CiAi exposes one immutable fluent builder for repository-level declarations:

```csharp
var definition = CiAi.ForRepository("TrentBest/MyRepository")
    .WithBadge(Badges.SithPolicy())
    .WithDependency(CiAi.Resolve("FSM_API", "runtime"))
    .WithDependency(CiAi.Logic.Or(
        "TrentBest/PrimaryProvider",
        "TrentBest/FallbackProvider"))
    .ExtendWith(Gauntlet.Registrar())
    .WithNamedDevelopmentPipeline("Core")
    .WithLLMAllocation(new CiAiLlmAllocation(
        "OpenAI", "model-id",
        maximumConcurrentCalls: 2,
        maximumCallsPerWindow: 30,
        maximumTokensPerWindow: 50_000))
    .Build();
```

The unbound entry point is also available when composing declarations before binding a repository:

```csharp
var definition = CiAi.WithBadge(Badges.SithPolicy())
    .WithDependency(CiAi.Resolve("FSM_API", "runtime"))
    .Build("TrentBest/MyRepository");
```

Both paths produce the same `CiAiRepositoryDefinition`. Each fluent call returns a new builder; prior definitions remain unchanged. Short dependency names resolve against the owner supplied to `Build`, while explicit `owner/repository` identities remain explicit. Fallback candidates retain their declared order.

## Separation of authority

- Badges, dependencies, extension identifiers, pipeline memberships, and allocation limits are declarations.
- `WithADP(...)` is optional. Ordinary CI composition does not imply autonomous-development authority.
- LLM budgets are repository requests, not provider-wide enforcement. A central allocator must enforce aggregate provider limits and fairness.
- The builder does not schedule repositories, invoke LLMs, execute CI, authenticate evidence, dispatch privileged workflows, or grant repository-write authority.
- Gauntlet registration is an opt-in declaration only; a separately trusted host remains responsible for any execution.

## Validation status

The unified builder and contract suite are implemented on `development`. The repository composition is connected to the single prerelease `.ciai/repository.json` manifest contract and its observer-profile validation. Badge providers and verified dependency evidence remain separate implementation work; this document does not claim those steps are complete.

## Provider-neutral badge declarations

A badge is a declarative provider registration, not a status authority. The public `CiAiBadgeDefinition` records:

- stable badge ID, display label, and provider ID;
- renderer and repository-relative output path;
- alternative text for accessibility;
- evidence-source identity and explicit unknown/unavailable state;
- optional workflow filename or HTTPS image/target URLs for static/external providers.

Third-party providers can return their own `CiAiBadgeDefinition` instances and register them through repeated `.WithBadge(...)` calls. CiAi does not need a provider-specific conditional, and the builder does not invoke provider code, fetch evidence, render files, or write README content.

The first non-workflow provider declaration is:

```csharp
var definition = CiAi.ForRepository("TrentBest/FSM_API_Benchmark")
    .WithBadge(Badges.FsmApi())
    .Build();
```

This declares the stable `fsm-api-powered` badge and its evidence contract. It does not yet claim verified FSM_API consumption or create the SVG. The status authority must separately verify the exact consumer commit and successful build; unknown, stale, or incomplete evidence must remain non-success.
