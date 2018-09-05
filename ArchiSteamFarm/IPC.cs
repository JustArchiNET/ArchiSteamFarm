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
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

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

		private static async Task Generate(this HttpResponse httpResponse, HttpStatusCode statusCode) {
			if (httpResponse == null) {
				ASF.ArchiLogger.LogNullError(nameof(httpResponse));
				return;
			}

			ushort statusCodeNumber = (ushort) statusCode;

			httpResponse.StatusCode = statusCodeNumber;
			await httpResponse.WriteAsync(statusCodeNumber + " - " + statusCode).ConfigureAwait(false);
		}

		[SuppressMessage("ReSharper", "ClassNeverInstantiated.Local")]
		private sealed class Startup {
			[SuppressMessage("ReSharper", "UnusedMember.Local")]
			public void Configure(IApplicationBuilder app, IHostingEnvironment env) {
				if ((app == null) || (env == null)) {
					ASF.ArchiLogger.LogNullError(nameof(app) + " || " + nameof(env));
					return;
				}

				app.UseStaticFiles();

				if (!string.IsNullOrEmpty(Program.GlobalConfig.IPCPassword)) {
					app.UseWhen(context => context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase), appBuilder => appBuilder.UseMiddleware<ApiAuthenticationMiddleware>());
				}

				RouteBuilder routeBuilder = new RouteBuilder(app);
				routeBuilder.MapGet("/Api/Debug", HandleApiDebugGet);

				app.UseRouter(routeBuilder.Build());
			}

			[SuppressMessage("ReSharper", "UnusedMember.Local")]
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
				private const byte MaxFailedAuthorizationAttempts = 5;

				private static readonly SemaphoreSlim AuthorizationSemaphore = new SemaphoreSlim(1, 1);
				private static readonly ConcurrentDictionary<IPAddress, byte> FailedAuthorizations = new ConcurrentDictionary<IPAddress, byte>();

				private readonly RequestDelegate Next;

				public ApiAuthenticationMiddleware(RequestDelegate next) => Next = next ?? throw new ArgumentNullException(nameof(next));

				[SuppressMessage("ReSharper", "UnusedMember.Local")]
				public async Task InvokeAsync(HttpContext context) {
					if (context == null) {
						ASF.ArchiLogger.LogNullError(nameof(context));
						return;
					}

					HttpStatusCode authenticationStatus = await GetAuthenticationStatus(context).ConfigureAwait(false);

					if (authenticationStatus != HttpStatusCode.OK) {
						await context.Response.Generate(authenticationStatus).ConfigureAwait(false);
						return;
					}

					await Next(context).ConfigureAwait(false);
				}

				private static async Task<HttpStatusCode> GetAuthenticationStatus(HttpContext context) {
					if (context == null) {
						ASF.ArchiLogger.LogNullError(nameof(context));
						return HttpStatusCode.InternalServerError;
					}

					if (string.IsNullOrEmpty(Program.GlobalConfig.IPCPassword)) {
						return HttpStatusCode.OK;
					}

					IPAddress clientIP = context.Connection.RemoteIpAddress;

					if (FailedAuthorizations.TryGetValue(clientIP, out byte attempts)) {
						if (attempts >= MaxFailedAuthorizationAttempts) {
							return HttpStatusCode.Forbidden;
						}
					}

					bool authorized;

					await AuthorizationSemaphore.WaitAsync().ConfigureAwait(false);

					try {
						if (FailedAuthorizations.TryGetValue(clientIP, out attempts)) {
							if (attempts >= MaxFailedAuthorizationAttempts) {
								return HttpStatusCode.Forbidden;
							}
						}

						if (!context.Request.Headers.TryGetValue("Authentication", out StringValues passwords) && !context.Request.Query.TryGetValue("password", out passwords)) {
							return HttpStatusCode.Unauthorized;
						}

						authorized = passwords.First() == Program.GlobalConfig.IPCPassword;

						if (authorized) {
							FailedAuthorizations.TryRemove(clientIP, out _);
						} else {
							FailedAuthorizations[clientIP] = FailedAuthorizations.TryGetValue(clientIP, out attempts) ? ++attempts : (byte) 1;
						}
					} finally {
						AuthorizationSemaphore.Release();
					}

					return authorized ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
				}
			}
		}
	}
}
