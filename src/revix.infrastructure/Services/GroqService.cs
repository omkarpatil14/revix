using System.Net.Http.Json;
using Revix.Core.Interfaces;
using Revix.Core.Models;
using Microsoft.Extensions.Configuration;

namespace Revix.Infrastructure.Services;

public class GroqService : IGroqService
{
    private readonly HttpClient _httpClient;

    public GroqService(HttpClient httpClient, IConfiguration config)
    {
        _httpClient = httpClient;
        _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {config["Groq:ApiKey"]}");
    }

    public async Task<string> ReviewCodeAsync(string language, string filename, string diff)
    {
        var prompt = $"""
        You are an expert {language} code reviewer. Review this code diff.

        File: {filename}
        Language: {language}

        Provide feedback on:
        - Bugs or logical errors
        - Security vulnerabilities
        - Performance issues
        - Code style and best practices for {language}
        - Missing error handling

        For each issue found, specify:
        - Severity: [Bug / Warning / Suggestion]
        - Line reference if possible
        - Clear explanation
        - How to fix it

        If the code looks good, say so briefly.
        Be concise and specific. No generic advice.

        Diff:
        {diff}
        """;

        var response = await _httpClient.PostAsJsonAsync(
            "https://api.groq.com/openai/v1/chat/completions",
            new {
                model = "llama-3.3-70b-versatile",
                max_tokens = 1000,
                messages = new[] {
                    new { role = "user", content = prompt }
                }
            });

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<GroqResponse>();
        return result!.Choices[0].Message.Content;
    }
}