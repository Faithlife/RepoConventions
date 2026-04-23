using System.Text.Json.Nodes;

namespace RepoConventions;

internal sealed record ConventionSettingsEvaluationContext(
	string ConfigurationFilePath,
	string RepositoryRoot,
	JsonNode? ParentSettings,
	bool EnableSettingsExpressions,
	string SourceConventionName,
	string ChildConventionPath)
{
	public string ConfigurationDirectory => Path.GetDirectoryName(ConfigurationFilePath)!;
}
