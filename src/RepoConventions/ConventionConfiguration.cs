using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace RepoConventions;

internal static class ConventionConfiguration
{
	public static IReadOnlyList<ConventionReference> Load(string path)
	{
		var configuration = LoadConfigurationFile(path);

		var references = new List<ConventionReference>();
		foreach (var convention in configuration.Conventions)
		{
			if (string.IsNullOrWhiteSpace(convention.Path))
				throw new InvalidOperationException($"Convention entries in '{path}' must include a non-empty 'path'.");

			references.Add(new ConventionReference(convention.Path, convention.Settings));
		}

		return references;
	}

	public static bool AddConventionPath(string configurationPath, string conventionPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(conventionPath);

		var configuration = File.Exists(configurationPath)
			? LoadConfigurationFile(configurationPath)
			: new ConfigurationFile { Conventions = [] };

		if (configuration.Conventions.Any(x => x.Path == conventionPath))
			return false;

		configuration.Conventions.Add(new ConventionRecord { Path = conventionPath });
		SaveConfigurationFile(configurationPath, configuration);
		return true;
	}

	private static ConfigurationFile LoadConfigurationFile(string path)
	{
		var yaml = File.ReadAllText(path);
		var json = s_yamlJsonSerializer.Serialize(s_yamlDeserializer.Deserialize(yaml));
		var configuration = JsonSerializer.Deserialize<ConfigurationFile>(json);

		if (configuration?.Conventions is null)
			throw new InvalidOperationException($"Configuration file '{path}' must contain a 'conventions' sequence.");

		return configuration;
	}

	private static void SaveConfigurationFile(string path, ConfigurationFile configuration)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		var yamlModel = new Dictionary<string, object?>
		{
			["conventions"] = configuration.Conventions.Select(ConvertConventionRecordToYamlModel).ToList(),
		};

		File.WriteAllText(path, s_yamlWriter.Serialize(yamlModel));
	}

	private static Dictionary<string, object?> ConvertConventionRecordToYamlModel(ConventionRecord convention)
	{
		var yamlModel = new Dictionary<string, object?>
		{
			["path"] = convention.Path,
		};

		if (convention.Settings is not null)
			yamlModel["settings"] = ConvertJsonNodeToYamlValue(convention.Settings);

		return yamlModel;
	}

	private static object? ConvertJsonNodeToYamlValue(JsonNode? node)
	{
		if (node is null)
			return null;

		using var jsonDocument = JsonDocument.Parse(node.ToJsonString());
		return ConvertJsonElementToYamlValue(jsonDocument.RootElement);
	}

	private static object? ConvertJsonElementToYamlValue(JsonElement element) =>
		element.ValueKind switch
		{
			JsonValueKind.Object => element.EnumerateObject().ToDictionary(static x => x.Name, static x => ConvertJsonElementToYamlValue(x.Value)),
			JsonValueKind.Array => element.EnumerateArray().Select(ConvertJsonElementToYamlValue).ToList(),
			JsonValueKind.String => element.GetString(),
			JsonValueKind.Number when element.TryGetInt64(out var int64Value) => int64Value,
			JsonValueKind.Number when element.TryGetDecimal(out var decimalValue) => decimalValue,
			JsonValueKind.Number => element.GetDouble(),
			JsonValueKind.True or JsonValueKind.False => element.GetBoolean(),
			JsonValueKind.Null => null,
			_ => throw new InvalidOperationException($"Unsupported JSON value kind '{element.ValueKind}'."),
		};

	private sealed class ConfigurationFile
	{
		[JsonPropertyName("conventions")]
		public List<ConventionRecord> Conventions { get; init; } = null!;
	}

	private sealed class ConventionRecord
	{
		[JsonPropertyName("path")]
		public string Path { get; init; } = "";

		[JsonPropertyName("settings")]
		public JsonNode? Settings { get; init; }
	}

	private static readonly ISerializer s_yamlJsonSerializer = new SerializerBuilder().JsonCompatible().Build();
	private static readonly ISerializer s_yamlWriter = new SerializerBuilder().Build();
	private static readonly IDeserializer s_yamlDeserializer = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
}
