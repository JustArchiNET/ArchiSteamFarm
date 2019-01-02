//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
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

using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class Events {
		internal static async Task OnBotShutdown() {
			if (Program.ProcessRequired || Bot.Bots.Values.Any(bot => bot.KeepRunning)) {
				return;
			}

			ASF.ArchiLogger.LogGenericInfo(Strings.NoBotsAreRunning);

			// We give user extra 5 seconds for eventual config changes
			await Task.Delay(5000).ConfigureAwait(false);

			if (Program.ProcessRequired || Bot.Bots.Values.Any(bot => bot.KeepRunning)) {
				return;
			}

			await Program.Exit().ConfigureAwait(false);
		}
	}
}
