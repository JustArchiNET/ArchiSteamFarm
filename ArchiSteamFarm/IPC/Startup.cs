//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using ArchiSteamFarm.IPC.Middleware;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace ArchiSteamFarm.IPC {
	internal sealed class Startup {
		public void Configure(IApplicationBuilder app, IHostingEnvironment env) {
			if ((app == null) || (env == null)) {
				ASF.ArchiLogger.LogNullError(nameof(app) + " || " + nameof(env));
				return;
			}

			app.UseResponseCompression();

			if (!string.IsNullOrEmpty(Program.GlobalConfig.IPCPassword)) {
				app.UseWhen(context => context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase), appBuilder => appBuilder.UseMiddleware<ApiAuthenticationMiddleware>());
			}

			app.UseWebSockets();
			app.UseStaticFiles();
			app.UseMvcWithDefaultRoute();
		}

		public void ConfigureServices(IServiceCollection services) {
			if (services == null) {
				ASF.ArchiLogger.LogNullError(nameof(services));
				return;
			}

			services.AddResponseCompression();

			// We declare only the absolute bare functionality to get our IPC up and running
			IMvcCoreBuilder mvc = services.AddMvcCore();

			mvc.AddFormatterMappings();
			mvc.AddJsonFormatters();

			mvc.AddJsonOptions(
				options => {
					options.SerializerSettings.ContractResolver = new DefaultContractResolver();

					if (Debugging.IsUserDebugging) {
						options.SerializerSettings.Formatting = Formatting.Indented;
					}
				}
			);
		}
	}
}
