//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ArchiSteamFarm.Core;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers.Json;

public static class JsonUtilities {
	[PublicAPI]
	public static readonly JsonSerializerOptions DefaultJsonSerialierOptions = CreateDefaultJsonSerializerOption();

	[PublicAPI]
	public static readonly JsonSerializerOptions IndentedJsonSerialierOptions = CreateDefaultJsonSerializerOption(true);

	[PublicAPI]
	public static JsonSerializerOptions CreateDefaultJsonSerializerOption(bool writeIndented = false) =>
		new() {
			PropertyNamingPolicy = null,
			TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { ApplyCustomModifiers } },
			WriteIndented = writeIndented
		};

	private static void ApplyCustomModifiers(JsonTypeInfo jsonTypeInfo) {
		ArgumentNullException.ThrowIfNull(jsonTypeInfo);

		bool potentialDisallowedNullsPossible = false;

		foreach (JsonPropertyInfo property in jsonTypeInfo.Properties) {
			if (!potentialDisallowedNullsPossible && (property.Get != null) && (property.AttributeProvider?.IsDefined(typeof(JsonDisallowNullAttribute), false) == true)) {
				potentialDisallowedNullsPossible = true;
			}

			property.ShouldSerialize = (parent, _) => {
				bool? shouldSerialize = ShouldSerialize(parent, property);

				return shouldSerialize ?? true;
			};
		}

		if (potentialDisallowedNullsPossible) {
			jsonTypeInfo.OnDeserialized = OnPotentialDisallowedNullsDeserialized;
		}
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2075", Justification = "We don't care about trimmed properties, it's not like we can make it work differently")]
	private static void OnPotentialDisallowedNullsDeserialized(object obj) {
		ArgumentNullException.ThrowIfNull(obj);

		foreach (PropertyInfo property in obj.GetType().GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static).Where(property => (property.GetMethod != null) && property.IsDefined(typeof(JsonDisallowNullAttribute), false) && (property.GetValue(obj) == null))) {
			throw new JsonException($"Required property {property.Name} expects a non-null value.");
		}
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2075", Justification = "We don't care about trimmed properties, it's not like we can make it work differently")]
	private static bool? ShouldSerialize(object parent, JsonPropertyInfo property) {
		ArgumentNullException.ThrowIfNull(parent);
		ArgumentNullException.ThrowIfNull(property);

		try {
			// Handle most common case where ShouldSerializeXYZ() matches property name
			bool? shouldSerialize = ShouldSerialize(parent, property.Name);

			if (shouldSerialize.HasValue) {
				return shouldSerialize.Value;
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

			return !string.IsNullOrEmpty(memberName) ? ShouldSerialize(parent, memberName) : null;
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2075", Justification = "We don't care about trimmed methods, it's not like we can make it work differently")]
	private static bool? ShouldSerialize(object parent, string propertyName) {
		ArgumentNullException.ThrowIfNull(parent);
		ArgumentException.ThrowIfNullOrEmpty(propertyName);

		try {
			MethodInfo? shouldSerializeMethod = parent.GetType().GetMethod($"ShouldSerialize{propertyName}", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

			if ((shouldSerializeMethod == null) || (shouldSerializeMethod.ReturnType != typeof(bool))) {
				return null;
			}

			object? shouldSerialize = shouldSerializeMethod.Invoke(parent, null);

			if (shouldSerialize is bool result) {
				return result;
			}

			return null;
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}
}
