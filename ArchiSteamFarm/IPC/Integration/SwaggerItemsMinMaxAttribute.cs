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
using JetBrains.Annotations;
using Microsoft.OpenApi.Models;

namespace ArchiSteamFarm.IPC.Integration {
	[PublicAPI]
	public sealed class SwaggerItemsMinMaxAttribute : CustomSwaggerAttribute {
		public uint MaximumUint {
			get => BackingMaximum.HasValue ? decimal.ToUInt32(BackingMaximum.Value) : default(uint);
			set => BackingMaximum = value;
		}

		public uint MinimumUint {
			get => BackingMinimum.HasValue ? decimal.ToUInt32(BackingMinimum.Value) : default(uint);
			set => BackingMinimum = value;
		}

		private decimal? BackingMaximum;
		private decimal? BackingMinimum;

		public override void Apply(OpenApiSchema schema) {
			if (schema == null) {
				throw new ArgumentNullException(nameof(schema));
			}

			if (schema.Items == null) {
				throw new InvalidOperationException(nameof(schema.Items));
			}

			if (BackingMinimum.HasValue) {
				schema.Items.Minimum = BackingMinimum.Value;
			}

			if (BackingMaximum.HasValue) {
				schema.Items.Maximum = BackingMaximum.Value;
			}
		}
	}
}
