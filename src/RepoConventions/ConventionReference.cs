using System.Text.Json.Nodes;

namespace RepoConventions;

internal sealed record ConventionReference(string Path, JsonNode? Settings, PullRequestSettings? PullRequest = null);
