/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace ArchiSteamFarm {
	internal sealed class SteamSaleEvent : IDisposable {
		private const byte MaxSingleQueuesDaily = 3;

		private readonly Bot Bot;
		private readonly Timer SteamDiscoveryQueueTimer;

		internal SteamSaleEvent(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			if (!Debugging.IsDebugBuild) {
				return;
			}

			SteamDiscoveryQueueTimer = new Timer(
				async e => await ExploreDiscoveryQueue().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1 + 0.2 * Bot.Bots.Count), // Delay
				TimeSpan.FromHours(8.1) // Period
			);
		}

		public void Dispose() {
			SteamDiscoveryQueueTimer?.Dispose();
		}

		private async Task ExploreDiscoveryQueue() {
			if (!Bot.IsConnectedAndLoggedOn) {
				return;
			}

			for (byte i = 0; (i < MaxSingleQueuesDaily) && (await IsDiscoveryQueueAvailable().ConfigureAwait(false)).GetValueOrDefault(); i++) {
				HashSet<uint> queue = await Bot.ArchiWebHandler.GenerateNewDiscoveryQueue().ConfigureAwait(false);
				if (queue == null) {
					break;
				}

				// We could in theory do this in parallel, but who knows what would happen...
				foreach (uint queuedAppID in queue) {
					if (await Bot.ArchiWebHandler.ClearFromDiscoveryQueue(queuedAppID).ConfigureAwait(false)) {
						continue;
					}

					i = MaxSingleQueuesDaily;
					break;
				}
			}
		}

		private async Task<bool?> IsDiscoveryQueueAvailable() {
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetDiscoveryQueuePage().ConfigureAwait(false);
			if (htmlDocument == null) {
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='subtext']");
			if (htmlNode == null) {
				// Valid, no cards for exploring the queue available
				return false;
			}

			string text = htmlNode.InnerText;
			if (!string.IsNullOrEmpty(text)) {
				// It'd make more sense to check against "Come back tomorrow", but it might not cover out-of-the-event queue
				return text.StartsWith("You can get ", StringComparison.Ordinal);
			}

			Bot.ArchiLogger.LogNullError(nameof(text));
			return null;
		}
	}
}