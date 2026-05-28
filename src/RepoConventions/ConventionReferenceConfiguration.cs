using System.Text.Json.Nodes;

namespace RepoConventions;

internal sealed record ConventionReferenceConfiguration(JsonNode? Settings, PullRequestSettings? PullRequest, CommitSettings? Commit);
