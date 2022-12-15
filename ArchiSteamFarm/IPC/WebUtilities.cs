//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
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
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
#endif
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC;

internal static class WebUtilities {
#if NETFRAMEWORK
	internal static IMvcCoreBuilder AddControllers(this IServiceCollection services) {
		ArgumentNullException.ThrowIfNull(services);

		return services.AddMvcCore();
	}

	internal static IMvcCoreBuilder AddNewtonsoftJson(this IMvcCoreBuilder mvc, Action<MvcJsonOptions> setupAction) {
		ArgumentNullException.ThrowIfNull(mvc);
		ArgumentNullException.ThrowIfNull(setupAction);

		// Add JSON formatters that will be used as default ones if no specific formatters are asked for
		mvc.AddJsonFormatters();

		mvc.AddJsonOptions(setupAction);

		return mvc;
	}
#endif

	internal static string? GetUnifiedName(this Type type) {
		ArgumentNullException.ThrowIfNull(type);

		return type.GenericTypeArguments.Length == 0 ? type.FullName : $"{type.Namespace}.{type.Name}{string.Join("", type.GenericTypeArguments.Select(static innerType => $"[{innerType.GetUnifiedName()}]"))}";
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
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

		return Type.GetType($"{typeText},{typeText[..index]}");
	}

	internal static async Task WriteJsonAsync<TValue>(this HttpResponse response, TValue? value, JsonSerializerSettings? jsonSerializerSettings = null) {
		ArgumentNullException.ThrowIfNull(response);

		JsonSerializer serializer = JsonSerializer.CreateDefault(jsonSerializerSettings);

		response.ContentType = "application/json; charset=utf-8";

		StreamWriter streamWriter = new(response.Body, Encoding.UTF8);

		await using (streamWriter.ConfigureAwait(false)) {
#pragma warning disable CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose
			JsonTextWriter jsonWriter = new(streamWriter) {
				CloseOutput = false
			};
#pragma warning restore CA2000 // False positive, we're actually wrapping it in the using clause below exactly for that purpose

			await using (jsonWriter.ConfigureAwait(false)) {
				serializer.Serialize(jsonWriter, value);
			}
		}
	}
}
