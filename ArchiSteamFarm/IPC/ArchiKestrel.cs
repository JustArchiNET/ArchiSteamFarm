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
using System.IO;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.NLog;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NLog.Web;

namespace ArchiSteamFarm.IPC {
	internal static class ArchiKestrel {
		private const string ConfigurationFile = nameof(IPC) + ".config";

		internal static HistoryTarget HistoryTarget { get; private set; }

		private static IWebHost KestrelWebHost;

		internal static void OnNewHistoryTarget(HistoryTarget historyTarget = null) {
			if (HistoryTarget != null) {
				HistoryTarget.NewHistoryEntry -= LogController.OnNewHistoryEntry;
				HistoryTarget = null;
			}

			if (historyTarget != null) {
				historyTarget.NewHistoryEntry += LogController.OnNewHistoryEntry;
				HistoryTarget = historyTarget;
			}
		}

		internal static async Task Start() {
			if (KestrelWebHost != null) {
				return;
			}

			// The order of dependency injection matters, pay attention to it
			IWebHostBuilder builder = new WebHostBuilder();

			// Set default directories
			builder.UseContentRoot(SharedInfo.HomeDirectory);
			builder.UseWebRoot(SharedInfo.WebsiteDirectory);

			// Check if custom config is available
			string absoluteConfigDirectory = Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.ConfigDirectory);

			if (File.Exists(Path.Combine(absoluteConfigDirectory, ConfigurationFile))) {
				// Set up custom config to be used
				builder.UseConfiguration(new ConfigurationBuilder().SetBasePath(absoluteConfigDirectory).AddJsonFile(ConfigurationFile, false, true).Build());

				// Use custom config for Kestrel and Logging configuration
				builder.UseKestrel((builderContext, options) => options.Configure(builderContext.Configuration.GetSection("Kestrel")));
				builder.ConfigureLogging((hostingContext, logging) => logging.AddConfiguration(hostingContext.Configuration.GetSection("Logging")));
			} else {
				// Use ASF defaults for Kestrel and Logging
				builder.UseKestrel(options => options.ListenLocalhost(1242));
				builder.ConfigureLogging(logging => logging.SetMinimumLevel(Debugging.IsUserDebugging ? LogLevel.Trace : LogLevel.Warning));
			}

			// Enable NLog integration for logging
			builder.UseNLog();

			// Specify Startup class for IPC
			builder.UseStartup<Startup>();

			// Init history logger for /Api/Log usage
			Logging.InitHistoryLogger();

			// Start the server
			IWebHost kestrelWebHost = builder.Build();

			try {
				await kestrelWebHost.StartAsync().ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				kestrelWebHost.Dispose();
				return;
			}

			KestrelWebHost = kestrelWebHost;
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
