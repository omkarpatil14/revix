namespace Revix.Core.Models;
 
public class ReviewJob
{
    public string Owner      { get; set; } = string.Empty;
    public string Repo       { get; set; } = string.Empty;
    public int    PrNumber   { get; set; }
    public string PrTitle    { get; set; } = string.Empty;
    public string CommitSha  { get; set; } = string.Empty;
    public string RepoDbId   { get; set; } = string.Empty;
}
 