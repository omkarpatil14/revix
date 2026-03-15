namespace Revix.Core.Models;

public class GitHubRepo
{
    public long   Id       { get; set; }
    public string Name     { get; set; } = string.Empty;  
    public string FullName { get; set; } = string.Empty;  
    public bool   Private  { get; set; }
    public string HtmlUrl  { get; set; } = string.Empty;
}