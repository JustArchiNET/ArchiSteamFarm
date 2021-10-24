//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// |
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// |
// http://www.apache.org/licenses/LICENSE-2.0
// |
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif
using System;
using System.Collections.Immutable;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AngleSharp.Dom;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm.Steam.Integration {
	internal sealed class SteamSaleEvent : IAsyncDisposable {
		private const byte MaxSingleQueuesDaily = 3; // This is only a failsafe for infinite queue clearing (in case IsDiscoveryQueueAvailable() would fail us)

		private readonly Bot Bot;

#pragma warning disable CA2213 // False positive, .NET Framework can't understand DisposeAsync()
		private readonly Timer SaleEventTimer;
#pragma warning restore CA2213 // False positive, .NET Framework can't understand DisposeAsync()

		internal SteamSaleEvent(Bot bot) {
			Bot = bot ?? throw new ArgumentNullException(nameof(bot));

			SaleEventTimer = new Timer(
				ExploreDiscoveryQueue,
				null,
				TimeSpan.FromHours(1.1) + TimeSpan.FromSeconds(ASF.LoadBalancingDelay * Bot.Bots?.Count ?? 0), // Delay
				TimeSpan.FromHours(8.1) // Period
			);
		}

		public ValueTask DisposeAsync() => SaleEventTimer.DisposeAsync();

		private async void ExploreDiscoveryQueue(object? state = null) {
			if (!Bot.IsConnectedAndLoggedOn) {
				return;
			}

			Bot.ArchiLogger.LogGenericTrace(Strings.Starting);

			for (byte i = 0; (i < MaxSingleQueuesDaily) && Bot.IsConnectedAndLoggedOn && (await IsDiscoveryQueueAvailable().ConfigureAwait(false)).GetValueOrDefault(); i++) {
				ImmutableHashSet<uint>? queue = await Bot.ArchiWebHandler.GenerateNewDiscoveryQueue().ConfigureAwait(false);

				if ((queue == null) || (queue.Count == 0)) {
					Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(queue)));

					break;
				}

				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.ClearingDiscoveryQueue, i));

				// We could in theory do this in parallel, but who knows what would happen...
				foreach (uint queuedAppID in queue) {
					if (await Bot.ArchiWebHandler.ClearFromDiscoveryQueue(queuedAppID).ConfigureAwait(false)) {
						continue;
					}

					Bot.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return;
				}

				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.DoneClearingDiscoveryQueue, i));
			}

			Bot.ArchiLogger.LogGenericTrace(Strings.Done);
		}

		private async Task<bool?> IsDiscoveryQueueAvailable() {
			using IDocument? htmlDocument = await Bot.ArchiWebHandler.GetDiscoveryQueuePage().ConfigureAwait(false);

			if (htmlDocument == null) {
				return null;
			}

			IElement? htmlNode = htmlDocument.SelectSingleNode("//div[@class='subtext']");

			if (htmlNode == null) {
				// Valid, no cards for exploring the queue available
				return false;
			}

			string text = htmlNode.TextContent;

			if (string.IsNullOrEmpty(text)) {
				Bot.ArchiLogger.LogNullError(nameof(text));

				return null;
			}

			if (Debugging.IsUserDebugging) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Content, text));
			}

			// It'd make more sense to check against "Come back tomorrow", but it might not cover out-of-the-event queue
			return text.StartsWith("You can get ", StringComparison.Ordinal);
		}
	}
}
