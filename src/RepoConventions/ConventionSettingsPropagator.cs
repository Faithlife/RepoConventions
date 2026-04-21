using System.Text;
using System.Text.Json.Nodes;

namespace RepoConventions;

internal static class ConventionSettingsPropagator
{
	public static JsonNode? Resolve(JsonNode? templateSettings, JsonNode? parentSettings, string compositeConventionName, string childConventionPath)
	{
		var outcome = ResolveNode(templateSettings, parentSettings, "$", NodeContext.Root, compositeConventionName, childConventionPath);
		return outcome.Kind switch
		{
			ResolutionKind.Value => outcome.Value,
			ResolutionKind.Omit => null,
			ResolutionKind.Splice => throw new InvalidOperationException("Array splicing is only valid for array items."),
			_ => throw new InvalidOperationException($"Unsupported resolution kind '{outcome.Kind}'."),
		};
	}

	private static ResolutionOutcome ResolveNode(JsonNode? templateNode, JsonNode? parentSettings, string location, NodeContext context, string compositeConventionName, string childConventionPath)
	{
		if (templateNode is JsonObject templateObject)
		{
			var resolvedObject = new JsonObject();
			foreach (var property in templateObject)
			{
				var propertyOutcome = ResolveNode(property.Value, parentSettings, $"{location}.{property.Key}", NodeContext.ObjectProperty, compositeConventionName, childConventionPath);
				if (propertyOutcome.Kind == ResolutionKind.Omit)
					continue;

				if (propertyOutcome.Kind == ResolutionKind.Splice)
					throw new InvalidOperationException("Array splicing is only valid for array items.");

				resolvedObject[property.Key] = propertyOutcome.Value;
			}

			return ResolutionOutcome.FromValue(resolvedObject);
		}

		if (templateNode is JsonArray templateArray)
		{
			var resolvedArray = new JsonArray();
			for (var index = 0; index < templateArray.Count; index++)
			{
				var itemOutcome = ResolveNode(templateArray[index], parentSettings, $"{location}[{index}]", NodeContext.ArrayItem, compositeConventionName, childConventionPath);
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
			return ResolveStringValue(stringValue, parentSettings, location, context, compositeConventionName, childConventionPath);

		return ResolutionOutcome.FromValue(templateNode?.DeepClone());
	}

	private static ResolutionOutcome ResolveStringValue(string stringValue, JsonNode? parentSettings, string location, NodeContext context, string compositeConventionName, string childConventionPath)
	{
		var segments = ParseSegments(stringValue, location, compositeConventionName, childConventionPath);
		if (segments.Count == 0)
			return ResolutionOutcome.FromValue(JsonValue.Create(stringValue));

		if (segments.Count == 1 && segments[0] is ExpressionSegment exactExpression)
		{
			var exactResolution = ResolvePath(exactExpression.PathSegments, parentSettings, exactExpression.RawText, location, compositeConventionName, childConventionPath);
			if (exactResolution.IsMissing)
			{
				return context switch
				{
					NodeContext.ObjectProperty or NodeContext.ArrayItem => ResolutionOutcome.Omit(),
					_ => ResolutionOutcome.FromValue(null),
				};
			}

			if (context == NodeContext.ArrayItem && exactResolution.Value is JsonArray exactArray)
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
					var expressionResolution = ResolvePath(expression.PathSegments, parentSettings, expression.RawText, location, compositeConventionName, childConventionPath);
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
			segments.Add(new ExpressionSegment(rawExpression, ParsePathSegments(rawExpression, location, compositeConventionName, childConventionPath)));
			index = expressionEnd + 2;
		}

		return segments;
	}

	private static string[] ParsePathSegments(string rawExpression, string location, string compositeConventionName, string childConventionPath)
	{
		if (!rawExpression.StartsWith("${{", StringComparison.Ordinal) || !rawExpression.EndsWith("}}", StringComparison.Ordinal))
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "Unsupported settings expression syntax.");

		var expressionBody = rawExpression[3..^2].Trim();
		const string settingsPrefix = "settings.";
		if (!expressionBody.StartsWith(settingsPrefix, StringComparison.Ordinal))
			throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, "Settings expressions must start with 'settings.'.");

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

	private static PathResolution ResolvePath(IReadOnlyList<string> pathSegments, JsonNode? parentSettings, string rawExpression, string location, string compositeConventionName, string childConventionPath)
	{
		if (parentSettings is null)
			return PathResolution.Missing();

		JsonNode? current = parentSettings;
		for (var index = 0; index < pathSegments.Count; index++)
		{
			if (current is not JsonObject currentObject)
				throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, $"Cannot continue through non-object value at 'settings.{string.Join('.', pathSegments.Take(index))}'.");

			if (!currentObject.TryGetPropertyValue(pathSegments[index], out current))
				return PathResolution.Missing();

			if (index < pathSegments.Count - 1 && current is not JsonObject)
				throw CreateResolutionException(compositeConventionName, childConventionPath, location, rawExpression, $"Cannot continue through non-object value at 'settings.{string.Join('.', pathSegments.Take(index + 1))}'.");
		}

		return PathResolution.Found(current);
	}

	private static InvalidOperationException CreateResolutionException(string compositeConventionName, string childConventionPath, string location, string rawExpression, string reason) =>
		new($"Failed to resolve child settings for convention '{childConventionPath}' in composite '{compositeConventionName}' at '{location}' for expression '{rawExpression}': {reason}");

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

	private sealed record ExpressionSegment(string RawText, IReadOnlyList<string> PathSegments) : TemplateSegment;

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
