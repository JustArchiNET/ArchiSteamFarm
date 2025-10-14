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
using System.Text.Json.Nodes;
using JetBrains.Annotations;
using Microsoft.OpenApi;

namespace ArchiSteamFarm.IPC.Integration;

[PublicAPI]
public sealed class SwaggerValidValuesAttribute : CustomSwaggerAttribute {
	private const string ExtensionName = "x-valid-values";

	public int[]? ValidIntValues { get; init; }
	public string[]? ValidStringValues { get; init; }

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We're not creating json values with non-primitive types")]
	public override void Apply(OpenApiSchema schema) {
		ArgumentNullException.ThrowIfNull(schema);

		JsonArray validValues = [];

		if (ValidIntValues != null) {
			foreach (int value in ValidIntValues) {
				validValues.Add(JsonValue.Create(value));
			}
		}

		if (ValidStringValues != null) {
			foreach (string value in ValidStringValues) {
				validValues.Add(JsonValue.Create(value));
			}
		}

		if (schema.Items != null) {
			if (schema.Items is OpenApiSchema items) {
				items.AddExtension(ExtensionName, new JsonNodeExtension(validValues));
			} else if (schema.Items.Extensions != null) {
				schema.Items.Extensions[ExtensionName] = new JsonNodeExtension(validValues);
			} else {
				throw new InvalidOperationException(nameof(schema.Items));
			}

			return;
		}

		schema.AddExtension(ExtensionName, new JsonNodeExtension(validValues));
	}
}
