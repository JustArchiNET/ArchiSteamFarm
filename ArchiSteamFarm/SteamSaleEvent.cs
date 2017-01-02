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

/*
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using HtmlAgilityPack;

namespace ArchiSteamFarm {
	internal sealed class SteamSaleEvent : IDisposable {
		private static readonly DateTime SaleEndingDateUtc = new DateTime(2017, 1, 2, 18, 0, 0, DateTimeKind.Utc);

		private readonly Bot Bot;
		private readonly Timer SteamAwardsTimer;
		private readonly Timer SteamDiscoveryQueueTimer;

		internal SteamSaleEvent(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			Bot = bot;

			if (DateTime.UtcNow >= SaleEndingDateUtc) {
				return;
			}

			SteamAwardsTimer = new Timer(
				async e => await VoteForSteamAwards().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1 + 0.2 * Bot.Bots.Count), // Delay
				TimeSpan.FromHours(6.1) // Period
			);

			SteamDiscoveryQueueTimer = new Timer(
				async e => await ExploreDiscoveryQueue().ConfigureAwait(false),
				null,
				TimeSpan.FromMinutes(1 + 0.2 * Bot.Bots.Count), // Delay
				TimeSpan.FromHours(6.1) // Period
			);
		}

		public void Dispose() {
			SteamAwardsTimer?.Dispose();
			SteamDiscoveryQueueTimer?.Dispose();
		}

		private async Task ExploreDiscoveryQueue() {
			if (DateTime.UtcNow >= SaleEndingDateUtc) {
				return;
			}

			if (!Bot.ArchiWebHandler.Ready) {
				return;
			}

			for (byte i = 0; (i < 3) && !(await IsDiscoveryQueueEmpty().ConfigureAwait(false)).GetValueOrDefault(); i++) {
				HashSet<uint> queue = await Bot.ArchiWebHandler.GenerateNewDiscoveryQueue().ConfigureAwait(false);
				if (queue == null) {
					break;
				}

				// We could in theory do this in parallel, but who knows what would happen...
				foreach (uint queuedAppID in queue) {
					if (await Bot.ArchiWebHandler.ClearFromDiscoveryQueue(queuedAppID).ConfigureAwait(false)) {
						continue;
					}

					i = byte.MaxValue;
					break;
				}
			}
		}

		private async Task<bool?> IsDiscoveryQueueEmpty() {
			if (!Bot.ArchiWebHandler.Ready) {
				return null;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetDiscoveryQueuePage().ConfigureAwait(false);
			if (htmlDocument == null) {
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='subtext']");
			if (htmlNode == null) {
				// No cards for exploring the queue available
				return true;
			}

			string text = htmlNode.InnerText;
			if (!string.IsNullOrEmpty(text)) {
				// It'd make more sense to check "Come back tomorrow", but it might not cover out-of-the-event queue
				return !text.StartsWith("You can get ", StringComparison.Ordinal);
			}

			Bot.ArchiLogger.LogNullError(nameof(text));
			return null;
		}

		private async Task VoteForSteamAwards() {
			if (DateTime.UtcNow >= SaleEndingDateUtc) {
				return;
			}

			if (!Bot.ArchiWebHandler.Ready) {
				return;
			}

			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetSteamAwardsPage().ConfigureAwait(false);

			HtmlNodeCollection nominationsNodes = htmlDocument?.DocumentNode.SelectNodes("//div[@class='vote_nominations store_horizontal_autoslider']");
			if (nominationsNodes == null) {
				// Event ended, error or likewise
				return;
			}

			foreach (HtmlNode nominationsNode in nominationsNodes) {
				HtmlNode myVoteNode = nominationsNode.SelectSingleNode("./div[@class='vote_nomination your_vote']");
				if (myVoteNode != null) {
					// Already voted
					continue;
				}

				string voteIDText = nominationsNode.GetAttributeValue("data-voteid", null);
				if (string.IsNullOrEmpty(voteIDText)) {
					Bot.ArchiLogger.LogNullError(nameof(voteIDText));
					return;
				}

				byte voteID;
				if (!byte.TryParse(voteIDText, out voteID) || (voteID == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(voteID));
					return;
				}

				HtmlNodeCollection voteNodes = nominationsNode.SelectNodes("./div[@class='vote_nomination ']");
				if (voteNodes == null) {
					Bot.ArchiLogger.LogNullError(nameof(voteNodes));
					return;
				}

				// Random a game we'll actually vote for, we don't want to make GabeN angry by rigging votes...
				HtmlNode voteNode = voteNodes[Utilities.RandomNext(voteNodes.Count)];

				string appIDText = voteNode.GetAttributeValue("data-vote-appid", null);
				if (string.IsNullOrEmpty(appIDText)) {
					Bot.ArchiLogger.LogNullError(nameof(appIDText));
					return;
				}

				uint appID;
				if (!uint.TryParse(appIDText, out appID) || (appID == 0)) {
					Bot.ArchiLogger.LogNullError(nameof(appID));
					return;
				}

				await Bot.ArchiWebHandler.SteamAwardsVote(voteID, appID).ConfigureAwait(false);
			}
		}
	}
}
*/