using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace RepoConventions;

internal static class ConventionConfiguration
{
	public static ConventionFileConfiguration Load(string path)
	{
		var configuration = LoadConfigurationFile(path);

		var references = new List<ConventionReference>();
		foreach (var convention in configuration.Conventions)
		{
			if (string.IsNullOrWhiteSpace(convention.Path))
				throw new InvalidOperationException($"Convention entries in '{path}' must include a non-empty 'path'.");

			references.Add(new ConventionReference(convention.Path, convention.Settings, ConvertPullRequestRecord(convention.PullRequest)));
		}

		return new ConventionFileConfiguration(references, ConvertPullRequestRecord(configuration.PullRequest));
	}

	public static bool AddConventionPath(string configurationPath, string conventionPath)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(conventionPath);

		if (!File.Exists(configurationPath))
		{
			SaveConfigurationFile(configurationPath, new ConfigurationFile
			{
				Conventions = [new ConventionRecord { Path = conventionPath }],
			});
			return true;
		}

		var yaml = File.ReadAllText(configurationPath);
		var configuration = LoadConfigurationText(configurationPath, yaml);

		if (configuration.Conventions.Any(x => x.Path == conventionPath))
			return false;

		var insertionPlan = DetermineConventionInsertionPlan(configurationPath, yaml);
		var updatedYaml = ApplyConventionInsertion(yaml, insertionPlan, conventionPath);
		ValidateConventionInsertion(configurationPath, conventionPath, configuration.Conventions.Count + 1, insertionPlan, updatedYaml);
		File.WriteAllText(configurationPath, NormalizeLineEndings(updatedYaml, GetNewLineSequence(yaml)));
		return true;
	}

	private static ConfigurationFile LoadConfigurationFile(string path) => LoadConfigurationText(path, File.ReadAllText(path));

	private static ConfigurationFile LoadConfigurationText(string path, string yaml)
	{
		try
		{
			var json = s_yamlJsonSerializer.Serialize(s_yamlDeserializer.Deserialize(yaml));
			var configuration = JsonSerializer.Deserialize<ConfigurationFile>(json);

			if (configuration?.Conventions is null)
				throw new InvalidOperationException($"Configuration file '{path}' must contain a 'conventions' sequence.");

			return configuration;
		}
		catch (YamlException ex)
		{
			throw new InvalidOperationException($"Configuration file '{path}' is not valid YAML: {ex.Message}", ex);
		}
	}

	private static ConventionInsertionPlan DetermineConventionInsertionPlan(string path, string yaml)
	{
		var parsingEvents = GetParsingEvents(path, yaml);
		var rootMappingIndex = parsingEvents.FindIndex(static x => x is MappingStart);
		if (rootMappingIndex < 0)
			throw new InvalidOperationException($"Configuration file '{path}' must contain a root mapping.");

		var currentIndex = rootMappingIndex + 1;
		while (currentIndex < parsingEvents.Count && parsingEvents[currentIndex] is not MappingEnd)
		{
			if (parsingEvents[currentIndex] is not Scalar keyEvent)
				throw new InvalidOperationException($"Configuration file '{path}' must contain scalar mapping keys.");

			var valueIndex = currentIndex + 1;
			if (keyEvent.Value == "conventions")
				return DetermineConventionInsertionPlan(path, yaml, keyEvent, valueIndex, parsingEvents);

			currentIndex = SkipNode(parsingEvents, valueIndex);
		}

		throw new InvalidOperationException($"Configuration file '{path}' must contain a 'conventions' sequence.");
	}

	private static ConventionInsertionPlan DetermineConventionInsertionPlan(string path, string yaml, Scalar keyEvent, int valueIndex, List<ParsingEvent> parsingEvents)
	{
		if (valueIndex >= parsingEvents.Count || parsingEvents[valueIndex] is not SequenceStart)
			throw new InvalidOperationException($"The 'conventions' entry in '{path}' must be a sequence to support 'repo-conventions add'.");

		var currentIndex = valueIndex + 1;
		var itemStartIndexes = new List<int>();
		while (currentIndex < parsingEvents.Count && parsingEvents[currentIndex] is not SequenceEnd)
		{
			itemStartIndexes.Add(currentIndex);
			currentIndex = SkipNode(parsingEvents, currentIndex);
		}

		if (currentIndex >= parsingEvents.Count || parsingEvents[currentIndex] is not SequenceEnd sequenceEnd)
			throw new InvalidOperationException($"Could not determine where to append to 'conventions' in '{path}'.");

		var keyLine = GetOneBasedLineNumber(keyEvent.Start);
		string? itemIndentation = null;
		if (itemStartIndexes.Count > 0)
		{
			itemIndentation = GetLineIndentation(yaml, GetZeroBasedLineNumber(parsingEvents[itemStartIndexes[0]].Start));
			var sequenceEndLineIndex = GetZeroBasedLineNumber(sequenceEnd.Start);
			var lastItemLineIndex = GetZeroBasedLineNumber(parsingEvents[itemStartIndexes[^1]].Start);
			var insertionLineIndex = FindSequenceAppendLineIndex(yaml, lastItemLineIndex, sequenceEndLineIndex, itemIndentation);
			return new ConventionInsertionPlan(
				ConventionInsertionKind.InsertBeforeLine,
				GetLineStartIndex(yaml, insertionLineIndex),
				0,
				itemIndentation,
				insertionLineIndex + 1);
		}

		var keyLineIndex = GetZeroBasedLineNumber(keyEvent.Start);
		itemIndentation ??= GetLineIndentation(yaml, keyLineIndex) + "  ";
		if (IsEmptyFlowSequence(yaml, keyLineIndex))
		{
			var lineStartIndex = GetLineStartIndex(yaml, keyLineIndex);
			var lineEndIndex = GetLineEndIndex(yaml, keyLineIndex);
			return new ConventionInsertionPlan(
				ConventionInsertionKind.ReplaceEmptyFlowSequence,
				lineStartIndex,
				lineEndIndex - lineStartIndex,
				itemIndentation,
				keyLine);
		}

		return new ConventionInsertionPlan(
			ConventionInsertionKind.InsertBeforeLine,
			GetLineStartIndex(yaml, keyLineIndex + 1),
			0,
			itemIndentation,
			keyLine + 1);
	}

	private static string ApplyConventionInsertion(string yaml, ConventionInsertionPlan insertionPlan, string conventionPath)
	{
		var newLine = GetNewLineSequence(yaml);
		var conventionLine = $"{insertionPlan.ItemIndentation}- path: {conventionPath}";

		if (insertionPlan.Kind == ConventionInsertionKind.ReplaceEmptyFlowSequence)
		{
			var keyLine = yaml.Substring(insertionPlan.Index, insertionPlan.Length);
			var emptySequenceIndex = keyLine.IndexOf("[]", StringComparison.Ordinal);
			if (emptySequenceIndex < 0)
				throw new InvalidOperationException($"Failed to locate the empty 'conventions' sequence text at line {insertionPlan.LineNumber}.");

			var before = keyLine[..emptySequenceIndex];
			var after = keyLine[(emptySequenceIndex + 2)..];
			var rewrittenKeyLine = after.Length == 0 ? before.TrimEnd() : before + after;
			var lineBreak = GetLineBreakText(yaml, insertionPlan.Index + insertionPlan.Length);
			return yaml[..insertionPlan.Index]
				+ rewrittenKeyLine
				+ (lineBreak.Length == 0 ? newLine : lineBreak)
				+ conventionLine
				+ newLine
				+ yaml[(insertionPlan.Index + insertionPlan.Length + lineBreak.Length)..];
		}

		var needsLeadingNewLine = insertionPlan.Index > 0 && yaml[insertionPlan.Index - 1] != '\n';
		return yaml[..insertionPlan.Index]
			+ (needsLeadingNewLine ? newLine : "")
			+ conventionLine
			+ newLine
			+ yaml[insertionPlan.Index..];
	}

	private static void ValidateConventionInsertion(string path, string conventionPath, int expectedConventionCount, ConventionInsertionPlan insertionPlan, string updatedYaml)
	{
		ConfigurationFile reparsedConfiguration;
		try
		{
			reparsedConfiguration = LoadConfigurationText(path, updatedYaml);
		}
		catch (Exception ex)
		{
			throw new InvalidOperationException($"Failed to add convention path '{conventionPath}' to '{path}'. The text patch at line {insertionPlan.LineNumber} did not reparse successfully: {ex.Message}", ex);
		}

		if (reparsedConfiguration.Conventions.Count != expectedConventionCount || !reparsedConfiguration.Conventions.Any(x => x.Path == conventionPath))
		{
			throw new InvalidOperationException($"Failed to add convention path '{conventionPath}' to '{path}'. The text patch at line {insertionPlan.LineNumber} reparsed, but the resulting configuration did not contain the expected conventions entry.");
		}
	}

	private static List<ParsingEvent> GetParsingEvents(string path, string yaml)
	{
		try
		{
			var parser = new Parser(new StringReader(yaml));
			var parsingEvents = new List<ParsingEvent>();
			while (parser.MoveNext())
			{
				if (parser.Current is not null)
					parsingEvents.Add(parser.Current);
			}

			return parsingEvents;
		}
		catch (YamlException ex)
		{
			throw new InvalidOperationException($"Configuration file '{path}' is not valid YAML: {ex.Message}", ex);
		}
	}

	private static int SkipNode(IReadOnlyList<ParsingEvent> parsingEvents, int index)
	{
		return parsingEvents[index] switch
		{
			Scalar => index + 1,
			MappingStart => SkipMapping(parsingEvents, index + 1),
			SequenceStart => SkipSequence(parsingEvents, index + 1),
			_ => throw new InvalidOperationException($"Unsupported YAML event '{parsingEvents[index].GetType().Name}'."),
		};
	}

	private static int SkipMapping(IReadOnlyList<ParsingEvent> parsingEvents, int index)
	{
		while (index < parsingEvents.Count && parsingEvents[index] is not MappingEnd)
		{
			index = SkipNode(parsingEvents, index);
			index = SkipNode(parsingEvents, index);
		}

		return index + 1;
	}

	private static int SkipSequence(IReadOnlyList<ParsingEvent> parsingEvents, int index)
	{
		while (index < parsingEvents.Count && parsingEvents[index] is not SequenceEnd)
			index = SkipNode(parsingEvents, index);

		return index + 1;
	}

	private static int FindSequenceAppendLineIndex(string yaml, int lastItemLineIndex, int sequenceEndLineIndex, string itemIndentation)
	{
		for (var lineIndex = lastItemLineIndex + 1; lineIndex < sequenceEndLineIndex; lineIndex++)
		{
			var line = GetLineText(yaml, lineIndex);
			var trimmedLine = line.TrimStart();
			if (trimmedLine.Length == 0)
				return lineIndex;

			var indentationLength = line.Length - trimmedLine.Length;
			if (trimmedLine.StartsWith('#'))
			{
				if (indentationLength <= itemIndentation.Length)
					return lineIndex;

				continue;
			}

			if (indentationLength <= itemIndentation.Length && !trimmedLine.StartsWith("- ", StringComparison.Ordinal))
				return lineIndex;
		}

		return sequenceEndLineIndex;
	}

	private static bool IsEmptyFlowSequence(string yaml, int lineIndex)
	{
		var line = GetLineText(yaml, lineIndex);
		return line.Contains("[]", StringComparison.Ordinal);
	}

	private static string GetLineIndentation(string yaml, int lineIndex)
	{
		var line = GetLineText(yaml, lineIndex);
		var trimmedLine = line.TrimStart();
		return line[..(line.Length - trimmedLine.Length)];
	}

	private static string GetLineText(string yaml, int lineIndex)
	{
		var lineStartIndex = GetLineStartIndex(yaml, lineIndex);
		var lineEndIndex = GetLineEndIndex(yaml, lineIndex);
		return yaml[lineStartIndex..lineEndIndex];
	}

	private static int GetLineStartIndex(string yaml, int lineIndex)
	{
		if (lineIndex <= 0)
			return 0;

		var currentLine = 0;
		for (var index = 0; index < yaml.Length; index++)
		{
			if (currentLine == lineIndex)
				return index;

			if (yaml[index] == '\n')
				currentLine++;
		}

		return yaml.Length;
	}

	private static int GetLineEndIndex(string yaml, int lineIndex)
	{
		var lineStartIndex = GetLineStartIndex(yaml, lineIndex);
		for (var index = lineStartIndex; index < yaml.Length; index++)
		{
			if (yaml[index] == '\r' || yaml[index] == '\n')
				return index;
		}

		return yaml.Length;
	}

	private static string GetLineBreakText(string yaml, int lineEndIndex)
	{
		if (lineEndIndex >= yaml.Length)
			return "";

		if (yaml[lineEndIndex] == '\r' && lineEndIndex + 1 < yaml.Length && yaml[lineEndIndex + 1] == '\n')
			return "\r\n";

		return yaml[lineEndIndex] == '\n' ? "\n" : "";
	}

	private static string GetNewLineSequence(string yaml) => yaml.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

	private static int GetZeroBasedLineNumber(Mark mark) => checked((int) mark.Line) - 1;

	private static int GetOneBasedLineNumber(Mark mark) => checked((int) mark.Line);

	private static void SaveConfigurationFile(string path, ConfigurationFile configuration)
	{
		Directory.CreateDirectory(Path.GetDirectoryName(path)!);

		var json = JsonSerializer.Serialize(configuration, s_jsonWriterOptions);
		var yamlModel = s_yamlDeserializer.Deserialize(new StringReader(json));

		File.WriteAllText(path, NormalizeLineEndings(s_yamlWriter.Serialize(yamlModel), "\n"));
	}

	private static string NormalizeLineEndings(string text, string newLine) => text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", newLine, StringComparison.Ordinal);

	private static PullRequestSettings? ConvertPullRequestRecord(PullRequestRecord? pullRequest) =>
		pullRequest is null
			? null
			: new PullRequestSettings(
				pullRequest.Labels,
				pullRequest.Reviewers,
				pullRequest.Assignees,
				pullRequest.Draft,
				pullRequest.AutoMerge,
				pullRequest.MergeMethod);

	private sealed class ConfigurationFile
	{
		[JsonPropertyName("pull-request")]
		public PullRequestRecord? PullRequest { get; init; }

		[JsonPropertyName("conventions")]
		public List<ConventionRecord> Conventions { get; init; } = null!;
	}

	private sealed class ConventionRecord
	{
		[JsonPropertyName("path")]
		public string Path { get; init; } = "";

		[JsonPropertyName("settings")]
		public JsonNode? Settings { get; init; }

		[JsonPropertyName("pull-request")]
		public PullRequestRecord? PullRequest { get; init; }
	}

	private sealed class PullRequestRecord
	{
		[JsonPropertyName("labels")]
		public List<string>? Labels { get; init; }

		[JsonPropertyName("reviewers")]
		public List<string>? Reviewers { get; init; }

		[JsonPropertyName("assignees")]
		public List<string>? Assignees { get; init; }

		[JsonPropertyName("draft")]
		public bool? Draft { get; init; }

		[JsonPropertyName("auto-merge")]
		public bool? AutoMerge { get; init; }

		[JsonPropertyName("merge-method")]
		public string? MergeMethod { get; init; }
	}

	private static readonly ISerializer s_yamlJsonSerializer = new SerializerBuilder().JsonCompatible().Build();
	private static readonly ISerializer s_yamlWriter = new SerializerBuilder().WithIndentedSequences().Build();
	private static readonly IDeserializer s_yamlDeserializer = new DeserializerBuilder().WithAttemptingUnquotedStringTypeDeserialization().Build();
	private static readonly JsonSerializerOptions s_jsonWriterOptions = new()
	{
		DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
	};

	private enum ConventionInsertionKind
	{
		InsertBeforeLine,
		ReplaceEmptyFlowSequence,
	}

	private sealed record ConventionInsertionPlan(ConventionInsertionKind Kind, int Index, int Length, string ItemIndentation, int LineNumber);
}
