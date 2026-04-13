using System.Globalization;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace RepoConventions;

internal static class ConventionConfiguration
{
	public static IReadOnlyList<ConventionReference> Load(string path)
	{
		using var reader = File.OpenText(path);
		var yaml = new YamlStream();
		yaml.Load(reader);

		if (yaml.Documents.Count == 0 || yaml.Documents[0].RootNode is not YamlMappingNode mapping)
			throw new InvalidOperationException($"Configuration file '{path}' must contain a YAML mapping.");

		if (!mapping.Children.TryGetValue(new YamlScalarNode("conventions"), out var conventionsNode) || conventionsNode is not YamlSequenceNode sequence)
			throw new InvalidOperationException($"Configuration file '{path}' must contain a 'conventions' sequence.");

		var references = new List<ConventionReference>();
		foreach (var child in sequence.Children)
		{
			if (child is not YamlMappingNode conventionMapping)
				throw new InvalidOperationException($"Convention entries in '{path}' must be mappings.");

			if (!conventionMapping.Children.TryGetValue(new YamlScalarNode("path"), out var pathNode) || pathNode is not YamlScalarNode pathScalar || string.IsNullOrWhiteSpace(pathScalar.Value))
				throw new InvalidOperationException($"Convention entries in '{path}' must include a non-empty 'path'.");

			conventionMapping.Children.TryGetValue(new YamlScalarNode("settings"), out var settingsNode);
			references.Add(new ConventionReference(pathScalar.Value!, settingsNode is null ? null : ConvertYamlNode(settingsNode)));
		}

		return references;
	}

	private static JsonNode? ConvertYamlNode(YamlNode node) =>
		node switch
		{
			YamlScalarNode scalar => ConvertScalar(scalar),
			YamlSequenceNode sequence => new JsonArray(sequence.Children.Select(ConvertYamlNode).ToArray()),
			YamlMappingNode mapping => new JsonObject(mapping.Children.ToDictionary(
				entry => ((YamlScalarNode) entry.Key).Value ?? "",
				entry => ConvertYamlNode(entry.Value))),
			_ => throw new InvalidOperationException("Unsupported YAML node type."),
		};

	private static JsonValue? ConvertScalar(YamlScalarNode scalar)
	{
		if (scalar.Value is null)
			return null;

		if (bool.TryParse(scalar.Value, out var boolean))
			return JsonValue.Create(boolean);

		if (long.TryParse(scalar.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
			return JsonValue.Create(integer);

		if (double.TryParse(scalar.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var floatingPoint))
			return JsonValue.Create(floatingPoint);

		return JsonValue.Create(scalar.Value);
	}
}
