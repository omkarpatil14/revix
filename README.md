# Revix 🤖

An AI-powered GitHub code review bot that automatically reviews pull requests using Groq LLM and posts inline comments directly on your PRs.

## What it does

When a pull request is opened or updated in a connected repository, Revix:
1. Receives a GitHub webhook event
2. Fetches all changed files in the PR
3. Sends each file's diff to Groq LLM for review
4. Posts inline comments on each file in the PR
5. Posts a summary comment with all findings
6. Saves all reviews to the database for history

## Tech Stack

- **Backend:** ASP.NET Core 9, C#
- **Database:** PostgreSQL (Supabase)
- **ORM:** Entity Framework Core
- **LLM:** Groq API (llama-3.3-70b-versatile)
- **GitHub Integration:** Octokit.NET
- **Auth:** GitHub OAuth
- **Resilience:** Polly (retry logic)
- **Encryption:** ASP.NET Data Protection

## Project Structure
```
revix/
├── src/
│   ├── revix.API/              # ASP.NET Core Web API
│   │   ├── Controllers/        # Auth, Webhook, Groq endpoints
│   │   └── Program.cs          # App configuration
│   ├── Revix.Core/             # Domain layer
│   │   ├── Entities/           # User, Repository, Review, ReviewComment
│   │   ├── Interfaces/         # Service contracts
│   │   └── Models/             # DTOs and payload models
│   └── Revix.Infrastructure/   # Implementation layer
│       ├── Services/           # GitHubService, GroqService, WebhookService, CommentService
│       └── Migrations/         # EF Core migrations
```

## Getting Started

### Prerequisites

- .NET 9 SDK
- PostgreSQL database (Supabase recommended)
- GitHub OAuth App
- Groq API key
- ngrok (for local development)

### 1. Clone the repository
```bash
git clone https://github.com/omkarpatil14/revix.git
cd revix
```

### 2. Set up user secrets
```bash
cd src/revix.API

dotnet user-secrets set "ConnectionStrings:DefaultConnection" "Host=...;Port=6543;Database=postgres;Username=...;Password=...;SSL Mode=Require;Trust Server Certificate=true;No Reset On Close=true;Command Timeout=60;Keepalive=10"
dotnet user-secrets set "GitHub:ClientId" "your_github_client_id"
dotnet user-secrets set "GitHub:ClientSecret" "your_github_client_secret"
dotnet user-secrets set "GitHub:WebhookSecret" "your_webhook_secret"
dotnet user-secrets set "Groq:ApiKey" "your_groq_api_key"
```

### 3. Set up GitHub OAuth App

- Go to GitHub → Settings → Developer Settings → OAuth Apps → New OAuth App
- Homepage URL: `http://localhost:5001`
- Callback URL: `http://localhost:5001/auth/callback`

### 4. Run database migrations
```bash
dotnet ef database update --project ..\Revix.Infrastructure --startup-project .
```

### 5. Run the app
```bash
dotnet run
```

### 6. Expose locally with ngrok
```bash
ngrok http 5001
```

### 7. Set up GitHub Webhook

- Go to your repo → Settings → Webhooks → Add webhook
- Payload URL: `https://your-ngrok-url/api/webhook`
- Content type: `application/json`
- Secret: same as `GitHub:WebhookSecret`
- Events: Pull requests

## How Reviews Work

Each file in a PR is reviewed for:
- 🔴 **Bug** — logical errors, null references, crashes
- 🟡 **Warning** — security issues, missing error handling
- 🟢 **Suggestion** — performance, code style, best practices

## API Endpoints

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/auth/login` | GitHub OAuth login |
| GET | `/auth/callback` | OAuth callback |
| POST | `/api/webhook` | GitHub webhook receiver |
| POST | `/api/groq/review` | Manual code review ] |

## Database Schema

- **Users** — GitHub authenticated users
- **Repositories** — connected repos per user
- **Reviews** — PR review records
- **ReviewComments** — individual file review comments

## License

MIT