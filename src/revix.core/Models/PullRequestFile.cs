namespace Revix.Core.Models;  
public class PullRequestFile
{
    public string FileName { get; set; } = null!;   
    public string Patch { get; set; } = null!;     
    public string Language { get; set; } = null!;   
    public string Status { get; set; } = null!;    
}