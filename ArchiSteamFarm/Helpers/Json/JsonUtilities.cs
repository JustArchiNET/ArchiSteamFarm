// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers.Json;

public static class JsonUtilities {
	[PublicAPI]
	public static readonly JsonSerializerOptions DefaultJsonSerializerOptions = CreateDefaultJsonSerializerOptions();

	[PublicAPI]
	public static readonly JsonSerializerOptions IndentedJsonSerializerOptions = CreateDefaultJsonSerializerOptions(true);

	[PublicAPI]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	public static JsonElement ToJsonElement<T>(this T obj) where T : notnull {
		ArgumentNullException.ThrowIfNull(obj);

		return JsonSerializer.SerializeToElement(obj, DefaultJsonSerializerOptions);
	}

	[PublicAPI]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	public static T? ToJsonObject<T>(this JsonElement jsonElement) => jsonElement.Deserialize<T>(DefaultJsonSerializerOptions);

	[PublicAPI]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	public static T? ToJsonObject<T>(this JsonNode jsonNode) => jsonNode.Deserialize<T>(DefaultJsonSerializerOptions);

	[PublicAPI]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	public static async ValueTask<T?> ToJsonObject<T>(this Stream stream, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(stream);

		return await JsonSerializer.DeserializeAsync<T>(stream, DefaultJsonSerializerOptions, cancellationToken).ConfigureAwait(false);
	}

	[PublicAPI]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	public static T? ToJsonObject<T>([StringSyntax(StringSyntaxAttribute.Json)] this string json) {
		ArgumentException.ThrowIfNullOrEmpty(json);

		return JsonSerializer.Deserialize<T>(json, DefaultJsonSerializerOptions);
	}

	[PublicAPI]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	public static string ToJsonText<T>(this T obj, bool writeIndented = false) => JsonSerializer.Serialize(obj, writeIndented ? IndentedJsonSerializerOptions : DefaultJsonSerializerOptions);

	private static void ApplyCustomModifiers(JsonTypeInfo jsonTypeInfo) {
		ArgumentNullException.ThrowIfNull(jsonTypeInfo);

		bool potentialDisallowedNullsPossible = false;

		foreach (JsonPropertyInfo property in jsonTypeInfo.Properties) {
			// All our modifications require a valid Get method on a property
			if (property.Get == null) {
				continue;
			}

			// The object should be validated against potential nulls if at least one property has [JsonDisallowNull] declared, avoid performance penalty otherwise
			if (property.AttributeProvider?.IsDefined(typeof(JsonDisallowNullAttribute), false) == true) {
				if (property.PropertyType.IsValueType && (Nullable.GetUnderlyingType(property.PropertyType) == null)) {
					// We should have no [JsonDisallowNull] declared on non-nullable types, this requires developer correction
					throw new InvalidOperationException(Strings.FormatWarningUnknownValuePleaseReport(nameof(JsonDisallowNullAttribute), $"{property.Name} ({jsonTypeInfo.Type})"));
				}

				potentialDisallowedNullsPossible = true;
			}

			// The property should be checked against ShouldSerialize if there is a valid method to invoke, avoid performance penalty otherwise
			MethodInfo? shouldSerializeMethod = GetShouldSerializeMethod(jsonTypeInfo.Type, property);

			if (shouldSerializeMethod != null) {
				property.ShouldSerialize = (parent, _) => ShouldSerialize(shouldSerializeMethod, parent);
			}
		}

		if (potentialDisallowedNullsPossible) {
			jsonTypeInfo.OnDeserialized = OnPotentialDisallowedNullsDeserialized;
		}
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private static JsonSerializerOptions CreateDefaultJsonSerializerOptions(bool writeIndented = false) =>
		new(JsonSerializerDefaults.Strict) {
			AllowTrailingCommas = true,
			Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
			IndentCharacter = '\t',
			IndentSize = 1,
			PropertyNamingPolicy = null,
			ReadCommentHandling = JsonCommentHandling.Skip,
			TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ApplyCustomModifiers } },
			UnmappedMemberHandling = JsonUnmappedMemberHandling.Skip,
			WriteIndented = writeIndented
		};

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2070", Justification = "We don't care about trimmed methods, it's not like we can make it work differently anyway")]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2075", Justification = "We don't care about trimmed properties, it's not like we can make it work differently anyway")]
	private static MethodInfo? GetShouldSerializeMethod([SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")] Type parent, JsonPropertyInfo property) {
		ArgumentNullException.ThrowIfNull(parent);
		ArgumentNullException.ThrowIfNull(property);

		// Handle most common case where ShouldSerializeXYZ() matches property name
		MethodInfo? result = parent.GetMethod($"ShouldSerialize{property.Name}", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

		if (result?.ReturnType == typeof(bool)) {
			// Method exists and returns a boolean, that's what we'd like to hear
			return result;
		}

		// Handle less common case where ShouldSerializeXYZ() matches original member name
		PropertyInfo? memberNameProperty = property.GetType().GetProperty("MemberName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

		if (memberNameProperty == null) {
			// Should never happen, investigate if it does
			throw new InvalidOperationException(nameof(memberNameProperty));
		}

		object? memberNameResult = memberNameProperty.GetValue(property);

		if (memberNameResult is not string memberName) {
			// Should never happen, investigate if it does
			throw new InvalidOperationException(nameof(memberName));
		}

		if (string.IsNullOrEmpty(memberName) || (memberName == property.Name)) {
			// We don't have anything to work with further, there is no ShouldSerialize() method
			return null;
		}

		result = parent.GetMethod($"ShouldSerialize{memberName}", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

		// Use alternative method if it exists and returns a boolean
		return result?.ReturnType == typeof(bool) ? result : null;
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2075", Justification = "We don't care about trimmed properties, it's not like we can make it work differently anyway")]
	private static void OnPotentialDisallowedNullsDeserialized(object obj) {
		ArgumentNullException.ThrowIfNull(obj);

		Type type = obj.GetType();

		foreach (FieldInfo field in type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(field => field.IsDefined(typeof(JsonDisallowNullAttribute), false) && (field.GetValue(obj) == null))) {
			throw new JsonException($"Required field {field.Name} expects a non-null value.");
		}

		foreach (PropertyInfo property in type.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(property => (property.GetMethod != null) && property.IsDefined(typeof(JsonDisallowNullAttribute), false) && (property.GetValue(obj) == null))) {
			throw new JsonException($"Required property {property.Name} expects a non-null value.");
		}
	}

	private static bool ShouldSerialize(MethodInfo shouldSerializeMethod, object parent) {
		ArgumentNullException.ThrowIfNull(shouldSerializeMethod);
		ArgumentNullException.ThrowIfNull(parent);

		if (shouldSerializeMethod.ReturnType != typeof(bool)) {
			throw new InvalidOperationException(nameof(shouldSerializeMethod));
		}

		object? shouldSerialize = shouldSerializeMethod.Invoke(parent, null);

		if (shouldSerialize is not bool result) {
			// Should not happen, we've already determined we have a method that returns a boolean
			throw new InvalidOperationException(nameof(shouldSerialize));
		}

		return result;
	}
}
