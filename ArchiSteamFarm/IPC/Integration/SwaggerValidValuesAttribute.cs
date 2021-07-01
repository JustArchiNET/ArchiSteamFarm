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
using System.Linq;
using JetBrains.Annotations;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;

namespace ArchiSteamFarm.IPC.Integration {
	[PublicAPI]
	public sealed class SwaggerValidValuesAttribute : CustomSwaggerAttribute {
		public int[]? ValidIntValues { get; set; }
		public string[]? ValidStringValues { get; set; }

		public override void Apply(OpenApiSchema schema) {
			if (schema == null) {
				throw new ArgumentNullException(nameof(schema));
			}

			OpenApiArray validValues = new();

			if (ValidIntValues != null) {
				validValues.AddRange(ValidIntValues.Select(type => new OpenApiInteger(type)));
			}

			if (ValidStringValues != null) {
				validValues.AddRange(ValidStringValues.Select(type => new OpenApiString(type)));
			}

			if (schema.Items is { Reference: null }) {
				schema.Items.AddExtension("x-valid-values", validValues);
			} else {
				schema.AddExtension("x-valid-values", validValues);
			}
		}
	}
}
