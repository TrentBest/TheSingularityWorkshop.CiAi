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

The unified builder and contract suite are implemented on `development`. The next integration step is to connect the definition to the existing `.ciai/repository.json` parser/serializer, observer-profile contract, and reusable enrollment workflow without introducing a second builder or a second manifest schema. This document does not claim those integration steps are complete.
