using System.Text;
using TheSingularityWorkshop.CiAi;
internal static class GitHubWorkItemEventTests
{
    private const string HeadSha="abcdef0123456789abcdef0123456789abcdef01";
    private const string BaseSha="123456789abcdef0123456789abcdef012345678";
    public static int Run()
    {
        var issue=GitHubWorkItemEvent.Parse("issues","delivery-issue-1",Encoding.UTF8.GetBytes(IssuePayload()));
        Assert(issue.ContractVersion==1 && issue.ItemKind=="issue" && issue.RepositoryId==123456 &&
            issue.Repository=="TrentBest/Physics" && issue.Number==17 &&
            issue.WorkItemIdentity=="123456:issue:ISSUE_NODE_17","issue identity was not normalized");
        Assert(issue.HeadSha is null && issue.BaseSha is null,"issue unexpectedly has PR revision fields");
        var pr=GitHubWorkItemEvent.Parse("pull_request","delivery-pr-1",Encoding.UTF8.GetBytes(PrPayload()));
        Assert(pr.ItemKind=="pull_request" && pr.Number==23 && pr.HeadSha==HeadSha && pr.BaseSha==BaseSha &&
            pr.HeadRef=="feature/sourcebook" && pr.BaseRef=="development","PR refs and exact SHAs were not preserved");
        Throws<NotSupportedException>(()=>GitHubWorkItemEvent.Parse("push","delivery-push",Encoding.UTF8.GetBytes("{}")));
        Throws<NotSupportedException>(()=>GitHubWorkItemEvent.Parse("issues","delivery-ignore",Encoding.UTF8.GetBytes(IssuePayload("milestoned"))));
        Throws<InvalidDataException>(()=>GitHubWorkItemEvent.Parse("pull_request","delivery-bad-sha",Encoding.UTF8.GetBytes(PrPayload("short"))));
        Throws<InvalidDataException>(()=>GitHubWorkItemEvent.Parse("issues","delivery-bad-url",Encoding.UTF8.GetBytes(IssuePayload(url:"https://example.com/TrentBest/Physics/issues/17"))));
        Throws<ArgumentException>(()=>GitHubWorkItemEvent.Parse("issues"," ",Encoding.UTF8.GetBytes(IssuePayload())));
        Throws<ArgumentException>(()=>GitHubWorkItemEvent.Parse("issues","delivery-large",new byte[1024*1024+1]));
        Console.WriteLine("PASS: 8 GitHub work-item event contract checks.");
        return 8;
    }
    private static string IssuePayload(string action="opened",string url="https://github.com/TrentBest/Physics/issues/17")=>$$"""
    { "action":"{{action}}", "repository":{"id":123456,"full_name":"TrentBest/Physics"},
      "issue":{"id":98765,"node_id":"ISSUE_NODE_17","number":17,"html_url":"{{url}}","updated_at":"2026-11-23T05:00:00Z"} }
    """;
    private static string PrPayload(string headSha=HeadSha)=>$$"""
    { "action":"synchronize", "repository":{"id":123456,"full_name":"TrentBest/Physics"},
      "pull_request":{"id":98766,"node_id":"PR_NODE_23","number":23,
        "html_url":"https://github.com/TrentBest/Physics/pull/23","updated_at":"2026-11-23T05:10:00Z",
        "head":{"ref":"feature/sourcebook","sha":"{{headSha}}"},
        "base":{"ref":"development","sha":"{{BaseSha}}"} } }
    """;
    private static void Assert(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
    private static void Throws<T>(Action action) where T:Exception{try{action();}catch(T){return;}throw new InvalidOperationException($"Expected {typeof(T).Name}.");}
}