using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace RepoConventions;

internal static class ConventionSettingsPropagator
{
	public static JsonNode? Resolve(JsonNode? templateSettings, ConventionSettingsEvaluationContext context)
	{
		var outcome = ResolveNode(templateSettings, context, "$", NodeContext.Root);
		return outcome.Kind switch
		{
			ResolutionKind.Value => outcome.Value,
			ResolutionKind.Omit => null,
			ResolutionKind.Splice => throw new ProgramException("Array splicing is only valid for array items."),
			_ => throw new InvalidOperationException($"Unsupported resolution kind '{outcome.Kind}'."),
		};
	}

	private static ResolutionOutcome ResolveNode(JsonNode? templateNode, ConventionSettingsEvaluationContext evaluationContext, string location, NodeContext nodeContext)
	{
		if (templateNode is JsonObject templateObject)
		{
			var resolvedObject = new JsonObject();
			foreach (var property in templateObject)
			{
				var propertyOutcome = ResolveNode(property.Value, evaluationContext, $"{location}.{property.Key}", NodeContext.ObjectProperty);
				if (propertyOutcome.Kind == ResolutionKind.Omit)
					continue;

				if (propertyOutcome.Kind == ResolutionKind.Splice)
					throw new ProgramException("Array splicing is only valid for array items.");

				resolvedObject[property.Key] = propertyOutcome.Value;
			}

			return ResolutionOutcome.FromValue(resolvedObject);
		}

		if (templateNode is JsonArray templateArray)
		{
			var resolvedArray = new JsonArray();
			for (var index = 0; index < templateArray.Count; index++)
			{
				var itemOutcome = ResolveNode(templateArray[index], evaluationContext, $"{location}[{index}]", NodeContext.ArrayItem);
				switch (itemOutcome.Kind)
				{
					case ResolutionKind.Omit:
						break;

					case ResolutionKind.Splice:
						foreach (var item in itemOutcome.Items!)
							resolvedArray.Add(item);
						break;

					case ResolutionKind.Value:
						resolvedArray.Add(itemOutcome.Value);
						break;

					default:
						throw new InvalidOperationException($"Unsupported resolution kind '{itemOutcome.Kind}'.");
				}
			}

			return ResolutionOutcome.FromValue(resolvedArray);
		}

		if (templateNode is JsonValue jsonValue && jsonValue.TryGetValue<string>(out var stringValue))
			return ResolveStringValue(stringValue, evaluationContext, location, nodeContext);

		return ResolutionOutcome.FromValue(templateNode?.DeepClone());
	}

	private static ResolutionOutcome ResolveStringValue(string stringValue, ConventionSettingsEvaluationContext evaluationContext, string location, NodeContext nodeContext)
	{
		var segments = ParseSegments(stringValue, location, evaluationContext.SourceConventionName, evaluationContext.ChildConventionPath);
		if (segments.Count == 0)
			return ResolutionOutcome.FromValue(JsonValue.Create(stringValue));

		if (segments.Count == 1 && segments[0] is ExpressionSegment exactExpression)
		{
			var exactResolution = ResolveExpression(exactExpression, evaluationContext, location);
			if (exactResolution.IsMissing)
			{
				return nodeContext switch
				{
					NodeContext.ObjectProperty or NodeContext.ArrayItem => ResolutionOutcome.Omit(),
					_ => ResolutionOutcome.FromValue(null),
				};
			}

			if (nodeContext == NodeContext.ArrayItem && exactResolution.Value is JsonArray exactArray)
				return ResolutionOutcome.Splice(exactArray.Select(static item => item?.DeepClone()).ToList());

			return ResolutionOutcome.FromValue(exactResolution.Value?.DeepClone());
		}

		var builder = new StringBuilder();
		foreach (var segment in segments)
		{
			switch (segment)
			{
				case LiteralSegment literal:
					builder.Append(literal.Text);
					break;

				case ExpressionSegment expression:
					var expressionResolution = ResolveExpression(expression, evaluationContext, location);
					if (expressionResolution.IsMissing)
						break;

					if (expressionResolution.Value is JsonValue expressionValue && expressionValue.TryGetValue<string>(out var embeddedStringValue))
						builder.Append(embeddedStringValue);
					else if (expressionResolution.Value is null)
						builder.Append("null");
					else
						builder.Append(expressionResolution.Value.ToJsonString());

					break;

				default:
					throw new InvalidOperationException($"Unsupported parsed segment type '{segment.GetType().Name}'.");
			}
		}

		return ResolutionOutcome.FromValue(JsonValue.Create(builder.ToString()));
	}

	private static List<TemplateSegment> ParseSegments(string value, string location, string compositeConventionName, string childConventionPath)
	{
		if (!value.Contains("${{", StringComparison.Ordinal))
			return [];

		var segments = new List<TemplateSegment>();
		var index = 0;
		while (index < value.Length)
		{
			var expressionStart = value.IndexOf("${{", index, StringComparison.Ordinal);
			if (expressionStart < 0)
			{
				if (index < value.Length)
					segments.Add(new LiteralSegment(value[index..]));
				break;
			}

			if (expressionStart > index)
				segments.Add(new LiteralSegment(value[index..expressionStart]));

			var expressionEnd = value.IndexOf("}}", expressionStart + 3, StringComparison.Ordinal);
			if (expressionEnd < 0)
				throw CreateResolutionException(compositeConventionName, childConventionPath, location, value[expressionStart..], "Unterminated settings expression.");

			var rawExpression = value[expressionStart..(expressionEnd + 2)];
			segments.Add(new ExpressionSegment(rawExpression, ParseExpression(rawExpression, location, compositeConventionName, childConventionPath)));
			index = expressionEnd + 2;
		}

		return segments;
	}

	private static TemplateExpression ParseExpression(string rawExpression, string location, string compositeConventionName, string childConventionPath)
	{
		if (!rawExpression.StartsWith("${{", StringComparison.Ordinal) || !rawExpression.EndsWith("}}", StringComparison.Ordinal))
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "Unsupported settings expression syntax.");

		var expressionBody = rawExpression[3..^2].Trim();
		if (expressionBody.StartsWith("settings.", StringComparison.Ordinal))
			return new SettingsPathExpression(ParsePathSegments(expressionBody, rawExpression, location, compositeConventionName, childConventionPath));

		if (expressionBody.StartsWith("readText", StringComparison.Ordinal))
			return new ReadTextExpression(ParseReadTextPath(expressionBody, rawExpression, location, compositeConventionName, childConventionPath));

		throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "Unsupported settings expression syntax.");
	}

	private static string[] ParsePathSegments(string expressionBody, string rawExpression, string location, string compositeConventionName, string childConventionPath)
	{
		const string settingsPrefix = "settings.";
		var path = expressionBody[settingsPrefix.Length..];
		if (string.IsNullOrWhiteSpace(path))
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "Settings expressions must include at least one property name after 'settings.'.");

		var segments = path.Split('.', StringSplitOptions.None);
		if (segments.Any(static segment => string.IsNullOrWhiteSpace(segment)))
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "Settings expressions must use dotted property names with no empty segments.");

		if (segments.Any(static segment =>
			segment.Any(char.IsWhiteSpace) ||
			segment.Contains('[', StringComparison.Ordinal) ||
			segment.Contains(']', StringComparison.Ordinal) ||
			segment.Contains('\'', StringComparison.Ordinal) ||
			segment.Contains('"', StringComparison.Ordinal)))
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "Only dotted property paths are supported in settings expressions.");

		return segments;
	}

	private static string ParseReadTextPath(string expressionBody, string rawExpression, string location, string compositeConventionName, string childConventionPath)
	{
		const string functionName = "readText";
		var remainder = expressionBody[functionName.Length..].TrimStart();
		if (remainder.Length < 2 || remainder[0] != '(' || remainder[^1] != ')')
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "readText expressions must use the form readText(\"path\").");

		var argumentText = remainder[1..^1].Trim();
		if (argumentText.Length == 0)
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "readText requires a JSON string literal path argument.");

		try
		{
			var path = JsonSerializer.Deserialize<string>(argumentText)
				?? throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "readText requires a JSON string literal path argument.");
			return path;
		}
		catch (JsonException ex)
		{
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "readText requires a JSON string literal path argument.", ex);
		}
	}

	private static PathResolution ResolveExpression(ExpressionSegment expression, ConventionSettingsEvaluationContext context, string location)
	{
		return expression.Expression switch
		{
			SettingsPathExpression settingsPath => ResolveSettingsPath(settingsPath.PathSegments, context, expression.RawText, location),
			ReadTextExpression readText => ResolveReadText(readText.Path, context, expression.RawText, location),
			_ => throw new InvalidOperationException($"Unsupported expression type '{expression.Expression.GetType().Name}'."),
		};
	}

	private static PathResolution ResolveSettingsPath(IReadOnlyList<string> pathSegments, ConventionSettingsEvaluationContext context, string rawExpression, string location)
	{
		if (!context.EnableSettingsExpressions)
			return PathResolution.Found(JsonValue.Create(rawExpression));

		if (context.ParentSettings is null)
			return PathResolution.Missing();

		JsonNode? current = context.ParentSettings;
		for (var index = 0; index < pathSegments.Count; index++)
		{
			if (current is not JsonObject currentObject)
				throw CreateResolutionException(context.SourceConventionName, context.ChildConventionPath, location, rawExpression, $"Cannot continue through non-object value at 'settings.{string.Join('.', pathSegments.Take(index))}'.");

			if (!currentObject.TryGetPropertyValue(pathSegments[index], out current))
				return PathResolution.Missing();

			if (index < pathSegments.Count - 1 && current is not JsonObject)
				throw CreateResolutionException(context.SourceConventionName, context.ChildConventionPath, location, rawExpression, $"Cannot continue through non-object value at 'settings.{string.Join('.', pathSegments.Take(index + 1))}'.");
		}

		return PathResolution.Found(current);
	}

	private static PathResolution ResolveReadText(string path, ConventionSettingsEvaluationContext context, string rawExpression, string location)
	{
		try
		{
			return PathResolution.Found(JsonValue.Create(ConventionSettingsFileReader.ReadText(context, path)));
		}
		catch (ProgramException ex)
		{
			throw CreateResolutionException(context.SourceConventionName, context.ChildConventionPath, location, rawExpression, ex.Message, ex);
		}
	}

	private static ProgramException CreateResolutionException(string compositeConventionName, string childConventionPath, string location, string rawExpression, string reason) =>
		new($"Failed to resolve child settings for convention '{childConventionPath}' in composite '{compositeConventionName}' at '{location}' for expression '{rawExpression}': {reason}");

	private static ProgramException CreateResolutionException(string compositeConventionName, string childConventionPath, string location, string rawExpression, string reason, Exception innerException) =>
		new($"Failed to resolve child settings for convention '{childConventionPath}' in composite '{compositeConventionName}' at '{location}' for expression '{rawExpression}': {reason}", innerException);

	private enum NodeContext
	{
		Root,
		ObjectProperty,
		ArrayItem,
	}

	private enum ResolutionKind
	{
		Value,
		Omit,
		Splice,
	}

	private abstract record TemplateSegment;

	private sealed record LiteralSegment(string Text) : TemplateSegment;

	private sealed record ExpressionSegment(string RawText, TemplateExpression Expression) : TemplateSegment;

	private abstract record TemplateExpression;

	private sealed record SettingsPathExpression(IReadOnlyList<string> PathSegments) : TemplateExpression;

	private sealed record ReadTextExpression(string Path) : TemplateExpression;

	private sealed record PathResolution(JsonNode? Value, bool IsMissing)
	{
		public static PathResolution Found(JsonNode? value) => new(value, IsMissing: false);

		public static PathResolution Missing() => new(null, IsMissing: true);
	}

	private sealed record ResolutionOutcome(ResolutionKind Kind, JsonNode? Value, IReadOnlyList<JsonNode?>? Items)
	{
		public static ResolutionOutcome Omit() => new(ResolutionKind.Omit, null, null);

		public static ResolutionOutcome Splice(IReadOnlyList<JsonNode?> items) => new(ResolutionKind.Splice, null, items);

		public static ResolutionOutcome FromValue(JsonNode? value) => new(ResolutionKind.Value, value, null);
	}
}
