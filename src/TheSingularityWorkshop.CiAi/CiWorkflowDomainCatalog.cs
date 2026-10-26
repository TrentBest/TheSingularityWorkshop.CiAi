namespace TheSingularityWorkshop.CiAi;

/// <summary>Stable identifiers for workflow families in the CiAi domain map.</summary>
public static class CiWorkflowDomainCatalog
{
    private static readonly IReadOnlyList<CiWorkflowDomain> Domains = Array.AsReadOnly(new[]
    {
        D("build-test", "Build and test", "Compile, restore, run unit/integration tests, and preserve test evidence.", "source", "build", "test"),
        D("code-quality", "Code quality", "Lint, format, analyze, and enforce repository conventions.", "source", "quality-report"),
        D("security", "Security analysis", "Run dependency, secret, static-analysis, and vulnerability checks.", "source", "security-report"),
        D("compatibility", "Compatibility matrix", "Test supported frameworks, runtimes, platforms, and version combinations.", "source", "matrix-completeness"),
        D("documentation", "Documentation", "Validate links, examples, generated docs, branding, and required support material.", "source", "documentation-report"),
        D("package", "Package creation", "Build distributable packages and validate their expected outputs.", "source", "package-inventory"),
        D("release-candidate", "Release candidate", "Bind package identity, version, source revision, and digest into a reviewable candidate.", "candidate", "package-digest", "source-provenance"),
        D("publication", "Publication", "Publish only after explicit candidate-bound authorization and target checks.", "candidate", "authorization", "target"),
        D("deployment", "Deployment", "Promote a verified candidate to an explicitly named environment.", "candidate", "authorization", "environment"),
        D("deployment-validation", "Post-deployment validation", "Verify health, smoke tests, and deployment identity after promotion.", "deployment", "health", "smoke-test"),
        D("performance", "Performance and benchmarks", "Run named benchmarks, compare a defined baseline, and report regressions.", "source", "benchmark-result", "baseline"),
        D("integration", "Integration and contract tests", "Verify component boundaries, API contracts, and cross-repository compatibility.", "source", "contract-report"),
        D("artifact-handoff", "Artifact handoff", "Inventory, transfer, verify, acknowledge, and correlate artifacts without assuming trust.", "artifact", "digest", "receipt"),
        D("provenance", "Supply-chain provenance", "Bind outputs to source revisions, build runs, tools, and attestations.", "artifact", "source-provenance", "builder-identity"),
        D("scheduled-maintenance", "Scheduled maintenance", "Run recurring checks with explicit cadence, freshness, and missed-run behavior.", "schedule", "timestamp", "freshness"),
        D("concurrency", "Concurrency and cancellation", "Coordinate duplicate work, bounded parallelism, supersession, and cancellation.", "execution", "concurrency-key", "outcome"),
        D("recovery", "Retries and recovery", "Classify retryable failures and make replay, rollback, and compensation explicit.", "execution", "retry-policy", "idempotency-key"),
        D("multi-repository", "Multi-repository orchestration", "Coordinate dependency builds and verify exact upstream repository/revision evidence.", "repositories", "dependency-graph", "source-provenance"),
        D("matrix", "Matrix orchestration", "Expand bounded variants and prove all required matrix cells completed.", "matrix", "matrix-completeness"),
        D("observability", "Run diagnosis and reporting", "Explain completed, failed, skipped, cancelled, timed-out, and unknown work.", "run", "evidence", "diagnostic"),
        D("repository-hygiene", "Repository hygiene", "Check required files, branch conventions, metadata, and repository policy.", "repository", "policy-report"),
        D("smoke", "Minimal build/package smoke check", "Prove restore, build, and expected output existence with metadata checks.", "source", "build", "output-inventory")
    });

    private static readonly IReadOnlyList<string> CrossCutting = Array.AsReadOnly(new[]
    {
        "triggers", "dependencies-and-conditions", "typed-outcomes", "evidence-provenance",
        "policy-evaluation", "least-privilege", "candidate-bound-authorization",
        "idempotency-and-recovery", "observability", "provider-adapters", "declared-custom-capabilities"
    });

    public static IReadOnlyList<CiWorkflowDomain> All => Domains;
    public static IReadOnlyList<string> CrossCuttingCapabilities => CrossCutting;

    public static bool TryGet(string id, out CiWorkflowDomain? domain)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        domain = Domains.FirstOrDefault(item => string.Equals(item.Id, id, StringComparison.Ordinal));
        return domain is not null;
    }

    public static CiWorkflowDomain GetRequired(string id) =>
        TryGet(id, out var domain) ? domain! :
            throw new KeyNotFoundException($"Unknown CiAi workflow domain '{id}'.");

    private static CiWorkflowDomain D(string id, string name, string purpose, string subject, params string[] evidence) =>
        new(id, name, purpose, subject, Array.AsReadOnly(evidence));
}

/// <summary>A workflow-family entry in the domain catalog; required evidence labels are descriptive contracts.</summary>
public sealed record CiWorkflowDomain(
    string Id,
    string Name,
    string Purpose,
    string EvidenceSubject,
    IReadOnlyList<string> EvidenceKinds);
