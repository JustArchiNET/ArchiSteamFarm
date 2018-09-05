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
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ArchiSteamFarm {
	internal static class IPC {
		private const string ConfigurationFile = nameof(IPC) + SharedInfo.ConfigExtension;

		internal static bool IsRunning => KestrelWebHost != null;

		private static IWebHost KestrelWebHost;

		internal static void OnNewHistoryTarget(HistoryTarget historyTarget = null) {
			// TODO
		}

		internal static async Task Start() {
			if (KestrelWebHost != null) {
				return;
			}

			string absoluteConfigDirectory = Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.ConfigDirectory);

			bool hasCustomConfig = File.Exists(Path.Combine(absoluteConfigDirectory, ConfigurationFile));
			ASF.ArchiLogger.LogGenericDebug("hasCustomConfig? " + hasCustomConfig);

			IWebHostBuilder builder = new WebHostBuilder().UseStartup<Startup>().UseWebRoot(SharedInfo.WebsiteDirectory);

			if (hasCustomConfig) {
				builder = builder.UseConfiguration(new ConfigurationBuilder().SetBasePath(absoluteConfigDirectory).AddJsonFile(ConfigurationFile, false, true).Build());
				builder = builder.UseKestrel((builderContext, options) => options.Configure(builderContext.Configuration.GetSection("Kestrel")));
			} else {
				builder = builder.UseKestrel(options => options.ListenLocalhost(1242));
			}

			KestrelWebHost = builder.Build();
			await KestrelWebHost.StartAsync().ConfigureAwait(false);
		}

		internal static async Task Stop() {
			if (KestrelWebHost == null) {
				return;
			}

			await KestrelWebHost.StopAsync().ConfigureAwait(false);
			KestrelWebHost.Dispose();
			KestrelWebHost = null;
		}

		[SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
		private sealed class Startup {
			public void Configure(IApplicationBuilder app, IHostingEnvironment env) {
				if ((app == null) || (env == null)) {
					ASF.ArchiLogger.LogNullError(nameof(app) + " || " + nameof(env));
					return;
				}

				app.UseStaticFiles();

				app.MapWhen(context => context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase), appBuilder => appBuilder.UseMiddleware<ApiAuthenticationMiddleware>());

				RouteBuilder routeBuilder = new RouteBuilder(app);
				routeBuilder.MapGet("Api/Debug", HandleApiDebugGet);

				app.UseRouter(routeBuilder.Build());
			}

			public void ConfigureServices(IServiceCollection services) {
				if (services == null) {
					ASF.ArchiLogger.LogNullError(nameof(services));
					return;
				}

				services.AddRouting();
			}

			private async Task HandleApiDebugGet(HttpContext context) {
				await context.Response.WriteAsync("works").ConfigureAwait(false);
			}

			[SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
			private sealed class ApiAuthenticationMiddleware {
				private readonly RequestDelegate Next;

				public ApiAuthenticationMiddleware(RequestDelegate next) => Next = next ?? throw new ArgumentNullException(nameof(next));

				public async Task InvokeAsync(HttpContext context) {
					if (context == null) {
						ASF.ArchiLogger.LogNullError(nameof(context));
						return;
					}

					await Next(context).ConfigureAwait(false);
				}
			}
		}
	}
}
