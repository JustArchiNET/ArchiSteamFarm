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
using System.Globalization;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Integration;
using JetBrains.Annotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ArchiSteamFarm.IPC.OpenApi;

#pragma warning disable CA1812 // False positive, the class is used internally
[UsedImplicitly]
internal sealed class SchemaTransformer : IOpenApiSchemaTransformer {
	public Task TransformAsync(OpenApiSchema schema, OpenApiSchemaTransformerContext context, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentNullException.ThrowIfNull(context);

		ApplyCustomAttributes(schema, context);
		ApplyEnumDefinition(schema, context);

		return Task.CompletedTask;
	}

	private static void ApplyCustomAttributes(OpenApiSchema schema, OpenApiSchemaTransformerContext context) {
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentNullException.ThrowIfNull(context);

		if (context.JsonPropertyInfo?.AttributeProvider == null) {
			return;
		}

		foreach (CustomSwaggerAttribute customSwaggerAttribute in context.JsonPropertyInfo.AttributeProvider.GetCustomAttributes(typeof(CustomSwaggerAttribute), true)) {
			customSwaggerAttribute.Apply(schema);
		}
	}

	private static void ApplyEnumDefinition(OpenApiSchema schema, OpenApiSchemaTransformerContext context) {
		ArgumentNullException.ThrowIfNull(schema);
		ArgumentNullException.ThrowIfNull(context);

		if (context.JsonTypeInfo.Type is not { IsEnum: true }) {
			return;
		}

		if (context.JsonTypeInfo.Type.IsDefined(typeof(FlagsAttribute), false)) {
			schema.Format = "flags";
		}

		JsonObject definition = new();

		foreach (object? enumValue in context.JsonTypeInfo.Type.GetEnumValuesAsUnderlyingType()) {
			if (enumValue == null) {
				throw new InvalidOperationException(nameof(enumValue));
			}

			string? enumName = Enum.GetName(context.JsonTypeInfo.Type, enumValue);

			if (string.IsNullOrEmpty(enumName)) {
				// Fallback
				enumName = enumValue.ToString();

				if (string.IsNullOrEmpty(enumName)) {
					throw new InvalidOperationException(nameof(enumName));
				}
			}

			if (definition.ContainsKey(enumName)) {
				// This is possible if we have multiple names for the same enum value, we'll ignore additional ones
				continue;
			}

			// OpenApi seems to support only int and long from underlying enum types: https://learn.microsoft.com/dotnet/csharp/language-reference/builtin-types/integral-numeric-types
			definition[enumName] = enumValue switch {
				sbyte value => JsonValue.Create((int) value),
				byte value => JsonValue.Create((int) value),
				short value => JsonValue.Create((int) value),
				ushort value => JsonValue.Create((int) value),
				int value => JsonValue.Create(value),
				uint value => JsonValue.Create((long) value),
				long value => JsonValue.Create(value),
				ulong value => JsonValue.Create(value.ToString(CultureInfo.InvariantCulture)),
				nint value when nint.Size <= 4 => JsonValue.Create((int) value),
				nint value when nint.Size <= 8 => JsonValue.Create((long) value),
				nint value => JsonValue.Create(value.ToString(CultureInfo.InvariantCulture)),
				nuint value when nuint.Size <= 4 => JsonValue.Create((long) value),
				nuint value => JsonValue.Create(value.ToString(CultureInfo.InvariantCulture)),
				_ => throw new InvalidOperationException(nameof(enumValue))
			};
		}

		schema.AddExtension("x-definition", new JsonNodeExtension(definition));
	}
}
#pragma warning restore CA1812 // False positive, the class is used internally
