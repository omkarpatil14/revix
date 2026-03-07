using System.Text.Json.Serialization;

namespace Revix.Core.Models;

public class GitHubWebhookPayload
{
    [JsonPropertyName("action")]
    public string Action { get; set; } = null!;

    [JsonPropertyName("number")]
    public int PrNumber { get; set; }

    [JsonPropertyName("pull_request")]
    public PullRequestInfo PullRequest { get; set; } = null!;

    [JsonPropertyName("repository")]
    public RepositoryInfo Repository { get; set; } = null!;
}

public class PullRequestInfo
{
    [JsonPropertyName("title")]
    public string Title { get; set; } = null!;

    [JsonPropertyName("head")]
    public HeadInfo Head { get; set; } = null!;
}

public class HeadInfo
{
    [JsonPropertyName("sha")]
    public string Sha { get; set; } = null!;
}

public class RepositoryInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = null!;

    [JsonPropertyName("owner")]
    public OwnerInfo Owner { get; set; } = null!;
}

public class OwnerInfo
{
    [JsonPropertyName("login")]
    public string Login { get; set; } = null!;
}