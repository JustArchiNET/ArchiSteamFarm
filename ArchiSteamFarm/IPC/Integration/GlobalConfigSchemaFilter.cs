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
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Extensions;
using Microsoft.OpenApi.Models;
using SteamKit2;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ArchiSteamFarm.IPC.Integration {
	[UsedImplicitly]
	internal sealed class GlobalConfigSchemaFilter : ISchemaFilter {
		public void Apply(OpenApiSchema schema, SchemaFilterContext context) {
			if (schema == null) {
				throw new ArgumentNullException(nameof(schema));
			}

			if (context == null) {
				throw new ArgumentNullException(nameof(context));
			}

			if (context.MemberInfo?.DeclaringType != typeof(GlobalConfig)) {
				return;
			}

			switch (context.MemberInfo.Name) {
				case nameof(GlobalConfig.Blacklist):
					schema.Items.Minimum = 1;
					schema.Items.Maximum = uint.MaxValue;

					break;
				case nameof(GlobalConfig.SteamOwnerID):
					schema.Maximum = new SteamID(uint.MaxValue, EUniverse.Public, EAccountType.Individual);
					schema.Minimum = new SteamID(1, EUniverse.Public, EAccountType.Individual);

					OpenApiArray validValues = new();

					validValues.Add(new OpenApiInteger(0));

					schema.AddExtension("x-valid-values", validValues);

					break;
			}
		}
	}
}
