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

using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace ArchiSteamFarm {
	internal static class IPC {
		internal static bool IsRunning => false; // TODO

		internal static void OnNewHistoryTarget(HistoryTarget historyTarget = null) {
			// TODO
		}

		internal static async Task Start(IReadOnlyCollection<string> prefixes) {
			if ((prefixes == null) || (prefixes.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(prefixes));
				return;
			}

			IWebHost test = new WebHostBuilder().UseUrls(string.Join(";", prefixes)).UseStartup<Startup>().UseWebRoot(SharedInfo.WebsiteDirectory).UseKestrel().Build();

			// TODO, this intentionally blocks for now (testing stuff)
			await test.StartAsync().ConfigureAwait(false);
		}

		internal static void Stop() {
			// TODO
		}

		private sealed class Startup {
			public void ConfigureServices(IServiceCollection services) {
			}

			public void Configure(IApplicationBuilder app, IHostingEnvironment env) {
				app.UseStaticFiles();
			}
		}
	}
}
