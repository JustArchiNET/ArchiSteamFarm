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
using System.Reflection;
using JetBrains.Annotations;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArchiSteamFarm.IPC.Integration {
	[UsedImplicitly]
	internal sealed class CustomAttributesSchemaFilter : ISchemaFilter {
		public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
			if (schema == null) {
				throw new ArgumentNullException(nameof(schema));
			}

			if (context == null) {
				throw new ArgumentNullException(nameof(context));
			}

			ICustomAttributeProvider attributesProvider;

			if (context.MemberInfo != null) {
				attributesProvider = context.MemberInfo;
			} else if (context.ParameterInfo != null) {
				attributesProvider = context.ParameterInfo;
			} else {
				return;
			}

			foreach (CustomSwaggerAttribute customSwaggerAttribute in attributesProvider.GetCustomAttributes(typeof(CustomSwaggerAttribute), true)) {
				customSwaggerAttribute.Apply(schema);
			}
		}
	}
}
