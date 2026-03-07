using System.Text.Json.Serialization;

namespace Revix.Core.Models;

public class GroqResponse
{
    [JsonPropertyName("choices")]
    public List<GroqChoice> Choices { get; set; } = [];
}

public class GroqChoice
{
    [JsonPropertyName("message")]
    public GroqMessage Message { get; set; } = new();
}

public class GroqMessage
{
    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;
}