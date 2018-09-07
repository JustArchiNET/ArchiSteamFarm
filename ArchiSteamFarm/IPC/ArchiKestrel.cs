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

using System.IO;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog.Web;

namespace ArchiSteamFarm.IPC {
	internal static class ArchiKestrel {
		private const string ConfigurationFile = nameof(ArchiKestrel) + SharedInfo.ConfigExtension;

		private static IWebHost KestrelWebHost;

		internal static async Task Generate(this HttpResponse httpResponse, HttpStatusCode statusCode) {
			if (httpResponse == null) {
				ASF.ArchiLogger.LogNullError(nameof(httpResponse));
				return;
			}

			ushort statusCodeNumber = (ushort) statusCode;

			httpResponse.StatusCode = statusCodeNumber;
			await httpResponse.WriteAsync(statusCodeNumber + " - " + statusCode).ConfigureAwait(false);
		}

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

			builder = builder.ConfigureLogging(
				logging => {
					logging.ClearProviders();
					logging.SetMinimumLevel(Debugging.IsUserDebugging ? LogLevel.Trace : LogLevel.Warning);
				}
			).UseNLog();

			KestrelWebHost = builder.Build();
			Logging.InitHistoryLogger();

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
	}
}
