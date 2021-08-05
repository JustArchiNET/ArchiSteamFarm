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

#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC {
	internal static class WebUtilities {
		internal static string? GetUnifiedName(this Type type) {
			if (type == null) {
				throw new ArgumentNullException(nameof(type));
			}

			return type.GenericTypeArguments.Length == 0 ? type.FullName : type.Namespace + "." + type.Name + string.Join("", type.GenericTypeArguments.Select(innerType => '[' + innerType.GetUnifiedName() + ']'));
		}

		internal static Type? ParseType(string typeText) {
			if (string.IsNullOrEmpty(typeText)) {
				throw new ArgumentNullException(nameof(typeText));
			}

			Type? targetType = Type.GetType(typeText);

			if (targetType != null) {
				return targetType;
			}

			// We can try one more time by trying to smartly guess the assembly name from the namespace, this will work for custom libraries like SteamKit2
			int index = typeText.IndexOf('.', StringComparison.Ordinal);

			if ((index <= 0) || (index >= typeText.Length - 1)) {
				return null;
			}

			return Type.GetType(typeText + "," + typeText[..index]);
		}

		internal static async Task WriteJsonAsync<TValue>(this HttpResponse response, TValue? value, JsonSerializerSettings? jsonSerializerSettings = null) {
			if (response == null) {
				throw new ArgumentNullException(nameof(response));
			}

			JsonSerializer serializer = JsonSerializer.CreateDefault(jsonSerializerSettings);

			response.ContentType = "application/json; charset=utf-8";

			StreamWriter streamWriter = new(response.Body, Encoding.UTF8);

			await using (streamWriter.ConfigureAwait(false)) {
				using JsonTextWriter jsonWriter = new(streamWriter) {
					CloseOutput = false
				};

				serializer.Serialize(jsonWriter, value);
			}
		}
	}
}
