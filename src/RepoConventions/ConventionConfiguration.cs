using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace RepoConventions;

internal static class ConventionConfiguration
{
	public static ConventionFileConfiguration Load(string path, bool requireConventions = true)
	{
		var configuration = LoadConfigurationFile(path, requireConventions);
		var displayPath = FormatPathForDisplay(path);

		var references = new List<ConventionReference>();
		foreach (var convention in configuration.Conventions)
		{
			if (string.IsNullOrWhiteSpace(convention.Path))
				throw new ProgramException($"Convention entries in '{displayPath}' must include a non-empty 'path'.");

			references.Add(new ConventionReference(convention.Path, convention.Settings, ConvertPullRequestRecord(convention.PullRequest), ConvertCommitRecord(convention.Commit)));
		}

		return new ConventionFileConfiguration(references, ConvertPullRequestRecord(configuration.PullRequest), ConvertCommitRecord(configuration.Commit));
	}

	public static ConventionReferenceConfiguration ParseConventionReferenceConfiguration(string yaml)
	{
		try
		{
			var yamlModel = s_yamlDeserializer.Deserialize(yaml);
			var json = s_yamlJsonSerializer.Serialize(yamlModel);
			var rootNode = JsonNode.Parse(json);
			if (rootNode is not JsonObject rootObject)
				throw new ProgramException("--with must be a YAML mapping.");

			foreach (var property in rootObject)
			{
				if (property.Key == "path")
					throw new ProgramException("--with cannot include 'path' because the convention path is provided as an argument.");

				if (property.Key is not ("settings" or "pull-request" or "commit"))
					throw new ProgramException($"--with contains unsupported convention reference key '{property.Key}'. Supported keys are: settings, pull-request, commit.");
			}

			var configuration = rootObject.Deserialize<ConventionReferenceConfigurationRecord>();
			return configuration is null
				? new ConventionReferenceConfiguration(Settings: null, PullRequest: null, Commit: null)
				: new ConventionReferenceConfiguration(configuration.Settings, ConvertPullRequestRecord(configuration.PullRequest), ConvertCommitRecord(configuration.Commit));
		}
		catch (YamlException ex)
		{
			throw new ProgramException($"--with is not valid YAML: {ex.Message}", ex);
		}
		catch (JsonException ex)
		{
			throw new ProgramException($"--with is not valid convention reference configuration: {ex.Message}", ex);
		}
	}

	public static bool AddConventionPath(string configurationPath, string conventionPath) =>
		AddConventionReference(configurationPath, new ConventionReference(conventionPath, Settings: null, PullRequest: null, Commit: null));

	public static bool AddConventionReference(string configurationPath, ConventionReference conventionReference)
	{
		ArgumentException.ThrowIfNullOrWhiteSpace(conventionReference.Path);

		if (!File.Exists(configurationPath))
		{
			SaveConfigurationFile(configurationPath, new ConfigurationFile
			{
				Conventions = [ConvertConventionReferenceRecord(conventionReference)],
			});
			return true;
		}

		var yaml = File.ReadAllText(configurationPath);
		var configuration = LoadConfigurationText(configurationPath, yaml);

		var existingConvention = configuration.Conventions.FirstOrDefault(x => x.Path == conventionReference.Path);
		if (existingConvention is not null)
		{
			if (HasSameConfiguration(existingConvention, conventionReference))
				return false;

			throw new ProgramException($"Convention path '{conventionReference.Path}' is already present in '{FormatPathForDisplay(configurationPath)}' with different configuration.");
		}

		var insertionPlan = DetermineConventionInsertionPlan(configurationPath, yaml);
		var updatedYaml = ApplyConventionInsertion(yaml, insertionPlan, conventionReference);
		ValidateConventionInsertion(configurationPath, conventionReference, configuration.Conventions.Count + 1, insertionPlan, updatedYaml);
		File.WriteAllText(configurationPath, NormalizeLineEndings(updatedYaml, GetNewLineSequence(yaml)));
		return true;
	}

	public static IReadOnlyList<ConventionReference> GetConventionReferencesToAdd(string configurationPath, IReadOnlyList<ConventionReference> conventionReferences)
	{
		var existingConventions = File.Exists(configurationPath)
			? LoadConfigurationFile(configurationPath, requireConventions: true).Conventions
			: new List<ConventionRecord>();

		var conventionReferencesToAdd = new List<ConventionReference>();
		foreach (var conventionReference in conventionReferences)
		{
			ArgumentException.ThrowIfNullOrWhiteSpace(conventionReference.Path);

			var existingConvention = existingConventions.FirstOrDefault(x => x.Path == conventionReference.Path);
			if (existingConvention is not null)
			{
				if (!HasSameConfiguration(existingConvention, conventionReference))
					throw new ProgramException($"Convention path '{conventionReference.Path}' is already present in '{FormatPathForDisplay(configurationPath)}' with different configuration.");

				continue;
			}

			var pendingConvention = conventionReferencesToAdd.FirstOrDefault(x => x.Path == conventionReference.Path);
			if (pendingConvention is not null)
			{
				if (!HasSameConfiguration(pendingConvention, conventionReference))
					throw new ProgramException($"Convention path '{conventionReference.Path}' was provided more than once with different configuration.");

				continue;
			}

			conventionReferencesToAdd.Add(conventionReference);
		}

		return conventionReferencesToAdd;
	}

	public static IReadOnlyList<string> GetConventionPathsToAdd(string configurationPath, IReadOnlyList<string> conventionPaths)
	{
		var conventionReferences = conventionPaths.Select(static x => new ConventionReference(x, Settings: null, PullRequest: null, Commit: null)).ToList();
		return GetConventionReferencesToAdd(configurationPath, conventionReferences).Select(static x => x.Path).ToList();
	}

	private static ConfigurationFile LoadConfigurationFile(string path, bool requireConventions) => LoadConfigurationText(path, File.ReadAllText(path), requireConventions);

	private static ConfigurationFile LoadConfigurationText(string path, string yaml, bool requireConventions = true)
	{
		var displayPath = FormatPathForDisplay(path);

		try
		{
			var json = s_yamlJsonSerializer.Serialize(s_yamlDeserializer.Deserialize(yaml));
			var configuration = JsonSerializer.Deserialize<ConfigurationFile>(json);

			if (configuration is null)
			{
				if (!requireConventions)
					return new ConfigurationFile { Conventions = [] };

				throw new ProgramException($"Configuration file '{displayPath}' must contain a 'conventions' sequence.");
			}

			if (configuration.Conventions is null)
			{
				if (!requireConventions)
					return new ConfigurationFile { PullRequest = configuration.PullRequest, Commit = configuration.Commit, Conventions = [] };

				throw new ProgramException($"Configuration file '{displayPath}' must contain a 'conventions' sequence.");
			}

			return configuration;
		}
		catch (YamlException ex)
		{
			throw new ProgramException($"Configuration file '{displayPath}' is not valid YAML: {ex.Message}", ex);
		}
	}

	private static ConventionInsertionPlan DetermineConventionInsertionPlan(string path, string yaml)
	{
		var displayPath = FormatPathForDisplay(path);
		var parsingEvents = GetParsingEvents(path, yaml);
		var rootMappingIndex = parsingEvents.FindIndex(static x => x is MappingStart);
		if (rootMappingIndex < 0)
			throw new ProgramException($"Configuration file '{displayPath}' must contain a root mapping.");

		var currentIndex = rootMappingIndex + 1;
		while (currentIndex < parsingEvents.Count && parsingEvents[currentIndex] is not MappingEnd)
		{
			if (parsingEvents[currentIndex] is not Scalar keyEvent)
				throw new ProgramException($"Configuration file '{displayPath}' must contain scalar mapping keys.");

			var valueIndex = currentIndex + 1;
			if (keyEvent.Value == "conventions")
				return DetermineConventionInsertionPlan(path, yaml, keyEvent, valueIndex, parsingEvents);

			currentIndex = SkipNode(parsingEvents, valueIndex);
		}

		throw new ProgramException($"Configuration file '{displayPath}' must contain a 'conventions' sequence.");
	}

	private static ConventionInsertionPlan DetermineConventionInsertionPlan(string path, string yaml, Scalar keyEvent, int valueIndex, List<ParsingEvent> parsingEvents)
	{
		var displayPath = FormatPathForDisplay(path);

		if (valueIndex >= parsingEvents.Count || parsingEvents[valueIndex] is not SequenceStart)
			throw new ProgramException($"The 'conventions' entry in '{displayPath}' must be a sequence to support 'repo-conventions add'.");

		var currentIndex = valueIndex + 1;
		var itemStartIndexes = new List<int>();
		while (currentIndex < parsingEvents.Count && parsingEvents[currentIndex] is not SequenceEnd)
		{
			itemStartIndexes.Add(currentIndex);
			currentIndex = SkipNode(parsingEvents, currentIndex);
		}

		if (currentIndex >= parsingEvents.Count || parsingEvents[currentIndex] is not SequenceEnd sequenceEnd)
			throw new ProgramException($"Could not determine where to append to 'conventions' in '{displayPath}'.");

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

	private static string ApplyConventionInsertion(string yaml, ConventionInsertionPlan insertionPlan, ConventionReference conventionReference)
	{
		var newLine = GetNewLineSequence(yaml);
		var conventionBlock = FormatConventionReferenceBlock(conventionReference, insertionPlan.ItemIndentation, newLine);

		if (insertionPlan.Kind == ConventionInsertionKind.ReplaceEmptyFlowSequence)
		{
			var keyLine = yaml.Substring(insertionPlan.Index, insertionPlan.Length);
			var emptySequenceIndex = keyLine.IndexOf("[]", StringComparison.Ordinal);
			if (emptySequenceIndex < 0)
				throw new ProgramException($"Failed to locate the empty 'conventions' sequence text at line {insertionPlan.LineNumber}.");

			var before = keyLine[..emptySequenceIndex];
			var after = keyLine[(emptySequenceIndex + 2)..];
			var rewrittenKeyLine = after.Length == 0 ? before.TrimEnd() : before + after;
			var lineBreak = GetLineBreakText(yaml, insertionPlan.Index + insertionPlan.Length);
			return yaml[..insertionPlan.Index]
				+ rewrittenKeyLine
				+ (lineBreak.Length == 0 ? newLine : lineBreak)
				+ conventionBlock
				+ yaml[(insertionPlan.Index + insertionPlan.Length + lineBreak.Length)..];
		}

		var needsLeadingNewLine = insertionPlan.Index > 0 && yaml[insertionPlan.Index - 1] != '\n';
		return yaml[..insertionPlan.Index]
			+ (needsLeadingNewLine ? newLine : "")
			+ conventionBlock
			+ yaml[insertionPlan.Index..];
	}

	private static void ValidateConventionInsertion(string path, ConventionReference conventionReference, int expectedConventionCount, ConventionInsertionPlan insertionPlan, string updatedYaml)
	{
		var displayPath = FormatPathForDisplay(path);
		ConfigurationFile reparsedConfiguration;
		try
		{
			reparsedConfiguration = LoadConfigurationText(path, updatedYaml);
		}
		catch (ProgramException ex)
		{
			throw new ProgramException($"Failed to add convention path '{conventionReference.Path}' to '{displayPath}'. The text patch at line {insertionPlan.LineNumber} did not reparse successfully: {ex.Message}", ex);
		}

		if (reparsedConfiguration.Conventions.Count != expectedConventionCount || !reparsedConfiguration.Conventions.Any(x => x.Path == conventionReference.Path && HasSameConfiguration(x, conventionReference)))
		{
			throw new ProgramException($"Failed to add convention path '{conventionReference.Path}' to '{displayPath}'. The text patch at line {insertionPlan.LineNumber} reparsed, but the resulting configuration did not contain the expected conventions entry.");
		}
	}

	private static string FormatConventionReferenceBlock(ConventionReference conventionReference, string itemIndentation, string newLine)
	{
		var json = JsonSerializer.Serialize(ConvertConventionReferenceRecord(conventionReference), s_jsonWriterOptions);
		var yamlModel = s_yamlDeserializer.Deserialize(new StringReader(json));
		var yaml = NormalizeLineEndings(s_yamlWriter.Serialize(yamlModel), "\n").TrimEnd('\r', '\n');
		var lines = yaml.Split('\n');
		if (lines.Length == 0 || lines[0].Length == 0)
			throw new ProgramException($"Failed to serialize convention path '{conventionReference.Path}'.");

		var builder = new StringBuilder()
			.Append(itemIndentation)
			.Append("- ")
			.Append(lines[0])
			.Append(newLine);

		foreach (var line in lines.Skip(1))
		{
			builder
				.Append(itemIndentation)
				.Append("  ")
				.Append(line)
				.Append(newLine);
		}

		return builder.ToString();
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
			throw new ProgramException($"Configuration file '{FormatPathForDisplay(path)}' is not valid YAML: {ex.Message}", ex);
		}
	}

	private static int SkipNode(IReadOnlyList<ParsingEvent> parsingEvents, int index)
	{
		return parsingEvents[index] switch
		{
			Scalar => index + 1,
			MappingStart => SkipMapping(parsingEvents, index + 1),
			SequenceStart => SkipSequence(parsingEvents, index + 1),
			_ => throw new ProgramException($"Unsupported YAML event '{parsingEvents[index].GetType().Name}'."),
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

	private static string FormatPathForDisplay(string path) => path.Replace('\\', '/');

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

	private static CommitSettings? ConvertCommitRecord(CommitRecord? commit)
	{
		if (commit is null)
			return null;

		var message = string.IsNullOrWhiteSpace(commit.Message) ? null : commit.Message;
		return message is null ? null : new CommitSettings(message);
	}

	private static ConventionRecord ConvertConventionReferenceRecord(ConventionReference conventionReference) =>
		new()
		{
			Path = conventionReference.Path,
			Settings = conventionReference.Settings?.DeepClone(),
			PullRequest = ConvertPullRequestSettings(conventionReference.PullRequest),
			Commit = ConvertCommitSettings(conventionReference.Commit),
		};

	private static PullRequestRecord? ConvertPullRequestSettings(PullRequestSettings? pullRequest) =>
		pullRequest is null
			? null
			: new PullRequestRecord
			{
				Labels = pullRequest.Labels?.ToList(),
				Reviewers = pullRequest.Reviewers?.ToList(),
				Assignees = pullRequest.Assignees?.ToList(),
				Draft = pullRequest.Draft,
				AutoMerge = pullRequest.AutoMerge,
				MergeMethod = pullRequest.MergeMethod,
			};

	private static CommitRecord? ConvertCommitSettings(CommitSettings? commit) =>
		commit is null ? null : new CommitRecord { Message = commit.Message };

	private static bool HasSameConfiguration(ConventionRecord existingConvention, ConventionReference conventionReference) =>
		JsonNode.DeepEquals(existingConvention.Settings, conventionReference.Settings) &&
		HasSamePullRequestSettings(ConvertPullRequestRecord(existingConvention.PullRequest), conventionReference.PullRequest) &&
		HasSameCommitSettings(ConvertCommitRecord(existingConvention.Commit), conventionReference.Commit);

	private static bool HasSameConfiguration(ConventionReference left, ConventionReference right) =>
		JsonNode.DeepEquals(left.Settings, right.Settings) &&
		HasSamePullRequestSettings(left.PullRequest, right.PullRequest) &&
		HasSameCommitSettings(left.Commit, right.Commit);

	private static bool HasSamePullRequestSettings(PullRequestSettings? left, PullRequestSettings? right)
	{
		if (left is null || right is null)
			return left is null && right is null;

		return HasSameStringList(left.Labels, right.Labels) &&
			HasSameStringList(left.Reviewers, right.Reviewers) &&
			HasSameStringList(left.Assignees, right.Assignees) &&
			left.Draft == right.Draft &&
			left.AutoMerge == right.AutoMerge &&
			left.MergeMethod == right.MergeMethod;
	}

	private static bool HasSameCommitSettings(CommitSettings? left, CommitSettings? right) =>
		left is null || right is null
			? left is null && right is null
			: left.Message == right.Message;

	private static bool HasSameStringList(IReadOnlyList<string>? left, IReadOnlyList<string>? right) =>
		left is null || right is null
			? left is null && right is null
			: left.SequenceEqual(right, StringComparer.Ordinal);

	private sealed class ConfigurationFile
	{
		[JsonPropertyName("pull-request")]
		public PullRequestRecord? PullRequest { get; init; }

		[JsonPropertyName("commit")]
		public CommitRecord? Commit { get; init; }

		[JsonPropertyName("conventions")]
		public List<ConventionRecord> Conventions { get; init; } = null!;
	}

	private sealed class ConventionReferenceConfigurationRecord
	{
		[JsonPropertyName("settings")]
		public JsonNode? Settings { get; init; }

		[JsonPropertyName("pull-request")]
		public PullRequestRecord? PullRequest { get; init; }

		[JsonPropertyName("commit")]
		public CommitRecord? Commit { get; init; }
	}

	private sealed class ConventionRecord
	{
		[JsonPropertyName("path")]
		public string Path { get; init; } = "";

		[JsonPropertyName("settings")]
		public JsonNode? Settings { get; init; }

		[JsonPropertyName("pull-request")]
		public PullRequestRecord? PullRequest { get; init; }

		[JsonPropertyName("commit")]
		public CommitRecord? Commit { get; init; }
	}

	private sealed class CommitRecord
	{
		[JsonPropertyName("message")]
		public string? Message { get; init; }
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
