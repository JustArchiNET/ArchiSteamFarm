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

			Bot.ArchiLogger.LogGenericDebug("Started!");

			for (byte i = 0; (i < 3) && !(await IsDiscoveryQueueEmpty().ConfigureAwait(false)).GetValueOrDefault(); i++) {
				Bot.ArchiLogger.LogGenericDebug("Getting new queue...");
				HashSet<uint> queue = await Bot.ArchiWebHandler.GenerateNewDiscoveryQueue().ConfigureAwait(false);
				if (queue == null) {
					Bot.ArchiLogger.LogGenericWarning("Aborting due to error!");
					break;
				}

				Bot.ArchiLogger.LogGenericDebug("We got new queue, clearing...");
				foreach (uint queuedAppID in queue) {
					Bot.ArchiLogger.LogGenericDebug("Clearing " + queuedAppID + "...");
					if (await Bot.ArchiWebHandler.ClearFromDiscoveryQueue(queuedAppID).ConfigureAwait(false)) {
						continue;
					}

					Bot.ArchiLogger.LogGenericWarning("Aborting due to error!");
					i = byte.MaxValue;
					break;
				}
			}

			Bot.ArchiLogger.LogGenericDebug("Done!");
		}

		private async Task<bool?> IsDiscoveryQueueEmpty() {
			if (!Bot.ArchiWebHandler.Ready) {
				return null;
			}

			Bot.ArchiLogger.LogGenericDebug("Checking if discovery queue is empty...");
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetDiscoveryQueuePage().ConfigureAwait(false);
			if (htmlDocument == null) {
				Bot.ArchiLogger.LogGenericDebug("Could not get discovery queue page, returning null");
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//div[@class='subtext']");
			if (htmlNode == null) {
				Bot.ArchiLogger.LogNullError(nameof(htmlNode));
				return null;
			}

			string text = htmlNode.InnerText;
			if (!string.IsNullOrEmpty(text)) {
				// It'd make more sense to check "Come back tomorrow", but it might not cover out-of-the-event queue
				Bot.ArchiLogger.LogGenericDebug("Our text is: " + text);
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

			Bot.ArchiLogger.LogGenericDebug("Getting SteamAwards page...");
			HtmlDocument htmlDocument = await Bot.ArchiWebHandler.GetSteamAwardsPage().ConfigureAwait(false);

			HtmlNodeCollection nominationsNodes = htmlDocument?.DocumentNode.SelectNodes("//div[@class='vote_nominations store_horizontal_autoslider']");
			if (nominationsNodes == null) {
				// Event ended, error or likewise
				Bot.ArchiLogger.LogGenericDebug("Could not get SteamAwards page, returning");
				return;
			}

			foreach (HtmlNode nominationsNode in nominationsNodes) {
				HtmlNode myVoteNode = nominationsNode.SelectSingleNode("./div[@class='vote_nomination your_vote']");
				if (myVoteNode != null) {
					// Already voted
					Bot.ArchiLogger.LogGenericDebug("We voted already, nothing to do");
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

				Bot.ArchiLogger.LogGenericDebug("Voting in #" + voteID + " for " + appID + "...");
				await Bot.ArchiWebHandler.SteamAwardsVote(voteID, appID).ConfigureAwait(false);
				Bot.ArchiLogger.LogGenericDebug("Done!");
			}
		}
	}
}