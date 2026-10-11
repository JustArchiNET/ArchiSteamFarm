// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;
using System.Threading;
using ArchiSteamFarm.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ArchiSteamFarm.IPC.OpenApi;

internal static class OpenApiSchemaReferenceIdProvider {
	private static Lazy<SchemaReferenceData>? ReferenceData;

	internal static string? CreateSchemaReferenceId(JsonTypeInfo jsonTypeInfo) {
		ArgumentNullException.ThrowIfNull(jsonTypeInfo);

		Type schemaType = Nullable.GetUnderlyingType(jsonTypeInfo.Type) ?? jsonTypeInfo.Type;
		SchemaReferenceData referenceData = ReferenceData?.Value ?? throw new InvalidOperationException(nameof(ReferenceData));

		return referenceData.InlineSchemaTypes.Contains(schemaType) ? null : OpenApiOptions.CreateDefaultSchemaReferenceId(jsonTypeInfo);
	}

	internal static void Initialize(IServiceProvider serviceProvider) {
		ArgumentNullException.ThrowIfNull(serviceProvider);

		ReferenceData = new Lazy<SchemaReferenceData>(() => FindInlineSchemaTypes(serviceProvider), LazyThreadSafetyMode.ExecutionAndPublication);
	}

	internal static bool TryGetDefaultValue(JsonPropertyInfo jsonPropertyInfo, out JsonNode? defaultValue) {
		ArgumentNullException.ThrowIfNull(jsonPropertyInfo);

		SchemaReferenceData referenceData = ReferenceData?.Value ?? throw new InvalidOperationException(nameof(ReferenceData));

		if (jsonPropertyInfo.AttributeProvider is not MemberInfo memberInfo) {
			defaultValue = null;

			return false;
		}

		(Module Module, int MetadataToken, Type PropertyType) key = (memberInfo.Module, memberInfo.MetadataToken, jsonPropertyInfo.PropertyType);

		if (referenceData.AmbiguousDefaultValues.Contains(key)) {
			defaultValue = null;

			return false;
		}

		return referenceData.DefaultValues.TryGetValue(key, out defaultValue);
	}

	private static SchemaReferenceData FindInlineSchemaTypes(IServiceProvider serviceProvider) {
		ArgumentNullException.ThrowIfNull(serviceProvider);

		JsonSerializerOptions jsonSerializerOptions = serviceProvider.GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;
		IApiDescriptionGroupCollectionProvider apiDescriptionProvider = serviceProvider.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
		SchemaReferenceData result = new();
		HashSet<Type> visitedTypes = [];

		foreach (ApiDescription apiDescription in apiDescriptionProvider.ApiDescriptionGroups.Items.SelectMany(static group => group.Items)) {
			foreach (ApiParameterDescription parameterDescription in apiDescription.ParameterDescriptions) {
				registerSchemaType(parameterDescription.Type);
			}

			foreach (ApiResponseType responseType in apiDescription.SupportedResponseTypes) {
				if (responseType.Type != null) {
					registerSchemaType(responseType.Type);
				}
			}
		}

		return result;

		void registerSchemaType(Type type) {
			ArgumentNullException.ThrowIfNull(type);

			type = Nullable.GetUnderlyingType(type) ?? type;

			if ((type == typeof(void)) || type.IsByRef || type.IsPointer || type.IsByRefLike || type.ContainsGenericParameters || !visitedTypes.Add(type)) {
				return;
			}

			JsonTypeInfo jsonTypeInfo;

			try {
				jsonTypeInfo = jsonSerializerOptions.GetTypeInfo(type);
			} catch (Exception e) when (e is NotSupportedException or ArgumentException) {
				ASF.ArchiLogger.LogGenericWarningException(e);
				result.InlineSchemaTypes.Add(type);

				return;
			}

			object? instance = null;
			bool hasDefaultInstance = (jsonTypeInfo.Kind == JsonTypeInfoKind.Object) && TryCreateDefaultInstance(type, out instance);

			foreach (JsonPropertyInfo propertyInfo in jsonTypeInfo.Properties) {
				Type memberType = propertyInfo.PropertyType;
				Type? nullableType = Nullable.GetUnderlyingType(memberType);

				object? rawDefaultValue = null;
				JsonNode? defaultValueSnapshot = null;
				bool hasDefaultValue = hasDefaultInstance && TryCreateDefaultValueSnapshot(propertyInfo, instance!, jsonSerializerOptions, out rawDefaultValue, out defaultValueSnapshot);

				bool hasDefaultSnapshot = hasDefaultValue && propertyInfo.AttributeProvider is MemberInfo memberInfo && RegisterDefaultValue(result, memberInfo, memberType, defaultValueSnapshot);

				if (!hasDefaultSnapshot || ((nullableType != null) && (rawDefaultValue == null)) || !IsDefaultValue(memberType, rawDefaultValue)) {
					result.InlineSchemaTypes.Add(nullableType ?? memberType);
				}

				registerSchemaType(memberType);
			}

			if (jsonTypeInfo.ElementType != null) {
				registerSchemaType(jsonTypeInfo.ElementType);
			}

			if (jsonTypeInfo.KeyType != null) {
				registerSchemaType(jsonTypeInfo.KeyType);
			}

			if (jsonTypeInfo.PolymorphismOptions != null) {
				foreach (JsonDerivedType derivedType in jsonTypeInfo.PolymorphismOptions.DerivedTypes) {
					registerSchemaType(derivedType.DerivedType);
				}
			}
		}
	}

	private static bool IsDefaultValue(Type type, object? value) {
		ArgumentNullException.ThrowIfNull(type);

		if ((Nullable.GetUnderlyingType(type) != null) || !type.IsValueType) {
			return value == null;
		}

		object? defaultValue = Array.CreateInstance(type, 1).GetValue(0);

		return Equals(value, defaultValue);
	}

	private static bool RegisterDefaultValue(SchemaReferenceData referenceData, MemberInfo memberInfo, Type propertyType, JsonNode? defaultValue) {
		ArgumentNullException.ThrowIfNull(referenceData);
		ArgumentNullException.ThrowIfNull(memberInfo);
		ArgumentNullException.ThrowIfNull(propertyType);

		bool resultByMember = false;

		(Module Module, int MetadataToken, Type PropertyType) key = (memberInfo.Module, memberInfo.MetadataToken, propertyType);

		if (!referenceData.AmbiguousDefaultValues.Contains(key)) {
			if (referenceData.DefaultValues.TryGetValue(key, out JsonNode? existingValue) && !JsonNode.DeepEquals(existingValue, defaultValue)) {
				referenceData.DefaultValues.Remove(key);
				referenceData.AmbiguousDefaultValues.Add(key);
			} else {
				referenceData.DefaultValues[key] = defaultValue;
				resultByMember = true;
			}
		}

		return resultByMember;
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2072", Justification = "OpenAPI only considers types already used by its JSON metadata")]
	private static bool TryCreateDefaultInstance(Type type, [NotNullWhen(true)] out object? instance) {
		ArgumentNullException.ThrowIfNull(type);

		if (type.IsAbstract || type.IsInterface || type.ContainsGenericParameters) {
			instance = null;

			return false;
		}

		try {
			instance = Activator.CreateInstance(type, true);

			return instance != null;
		} catch (Exception e) when (e is ArgumentException or MemberAccessException or NotSupportedException) {
			instance = null;

			return false;
		} catch (TargetInvocationException e) {
			ASF.ArchiLogger.LogGenericWarningException(e);

			instance = null;

			return false;
		}
	}

	private static bool TryCreateDefaultValueSnapshot(JsonPropertyInfo jsonPropertyInfo, object instance, JsonSerializerOptions jsonSerializerOptions, out object? rawDefaultValue, out JsonNode? defaultValue) {
		ArgumentNullException.ThrowIfNull(jsonPropertyInfo);
		ArgumentNullException.ThrowIfNull(instance);
		ArgumentNullException.ThrowIfNull(jsonSerializerOptions);

		if (jsonPropertyInfo.Get == null) {
			rawDefaultValue = null;
			defaultValue = null;

			return false;
		}

		try {
			rawDefaultValue = jsonPropertyInfo.Get(instance);

			defaultValue = rawDefaultValue == null ? null : JsonSerializer.SerializeToNode(rawDefaultValue, jsonPropertyInfo.PropertyType, jsonSerializerOptions);

			return true;
		} catch (Exception e) when (e is not OutOfMemoryException and not AccessViolationException) {
			ASF.ArchiLogger.LogGenericWarningException(e);

			rawDefaultValue = null;
			defaultValue = null;

			return false;
		}
	}

	private sealed class SchemaReferenceData {
		internal readonly HashSet<(Module Module, int MetadataToken, Type PropertyType)> AmbiguousDefaultValues = [];
		internal readonly Dictionary<(Module Module, int MetadataToken, Type PropertyType), JsonNode?> DefaultValues = [];
		internal readonly HashSet<Type> InlineSchemaTypes = [];
	}
}
