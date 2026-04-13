using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace RepoConventions;

internal static class ConventionConfiguration
{
	public static IReadOnlyList<ConventionReference> Load(string path)
	{
		var yaml = File.ReadAllText(path);
		var json = s_yamlSerializer.Serialize(s_yamlDeserializer.Deserialize(yaml));
		var configuration = JsonSerializer.Deserialize<ConfigurationFile>(json);

		if (configuration?.Conventions is null)
			throw new InvalidOperationException($"Configuration file '{path}' must contain a 'conventions' sequence.");

		var references = new List<ConventionReference>();
		foreach (var convention in configuration.Conventions)
		{
			if (string.IsNullOrWhiteSpace(convention.Path))
				throw new InvalidOperationException($"Convention entries in '{path}' must include a non-empty 'path'.");

			references.Add(new ConventionReference(convention.Path, convention.Settings));
		}

		return references;
	}

	private sealed class ConfigurationFile
	{
		[JsonPropertyName("conventions")]
		public List<ConventionRecord>? Conventions { get; init; }
	}

	private sealed class ConventionRecord
	{
		[JsonPropertyName("path")]
		public string Path { get; init; } = "";

		[JsonPropertyName("settings")]
		public JsonNode? Settings { get; init; }
	}

	private static readonly ISerializer s_yamlSerializer = new SerializerBuilder().JsonCompatible().Build();
	private static readonly IDeserializer s_yamlDeserializer = new DeserializerBuilder().Build();
}
