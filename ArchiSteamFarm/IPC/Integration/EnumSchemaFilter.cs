//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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
using JetBrains.Annotations;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArchiSteamFarm.IPC.Integration {
	[UsedImplicitly]
	internal sealed class EnumSchemaFilter : ISchemaFilter {
		public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
			if (schema == null) {
				throw new ArgumentNullException(nameof(schema));
			}

			if (context == null) {
				throw new ArgumentNullException(nameof(context));
			}

			if (context.Type is not { IsEnum: true }) {
				return;
			}

			if (context.Type.IsDefined(typeof(FlagsAttribute), false)) {
				schema.Format = "flags";
			}

			OpenApiObject definition = new();

			foreach (object? enumValue in context.Type.GetEnumValues()) {
				if (enumValue == null) {
					throw new InvalidOperationException(nameof(enumValue));
				}

				string? enumName = Enum.GetName(context.Type, enumValue);

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

				IOpenApiAny enumObject;

				if (TryCast(enumValue, out int intValue)) {
					enumObject = new OpenApiInteger(intValue);
				} else if (TryCast(enumValue, out long longValue)) {
					enumObject = new OpenApiLong(longValue);
				} else if (TryCast(enumValue, out ulong ulongValue)) {
					// OpenApi spec doesn't support ulongs as of now
					enumObject = new OpenApiString(ulongValue.ToString(CultureInfo.InvariantCulture));
				} else {
					throw new InvalidOperationException(nameof(enumValue));
				}

				definition.Add(enumName, enumObject);
			}

			schema.AddExtension("x-definition", definition);
		}

		private static bool TryCast<T>(object value, out T typedValue) where T : struct {
			if (value == null) {
				throw new ArgumentNullException(nameof(value));
			}

			try {
				typedValue = (T) Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);

				return true;
			} catch (InvalidCastException) {
				typedValue = default(T);

				return false;
			} catch (OverflowException) {
				typedValue = default(T);

				return false;
			}
		}
	}
}
