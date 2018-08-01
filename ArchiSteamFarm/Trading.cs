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
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal sealed class Trading : IDisposable {
		internal const byte MaxItemsPerTrade = byte.MaxValue; // This is due to limit on POST size in WebBrowser
		internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<ulong> IgnoredTrades = new ConcurrentHashSet<ulong>();
		private readonly SemaphoreSlim TradesSemaphore = new SemaphoreSlim(1, 1);

		private bool ParsingScheduled;

		internal Trading(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		public void Dispose() => TradesSemaphore.Dispose();

		internal void OnDisconnected() => IgnoredTrades.Clear();

		internal async Task OnNewTrade() {
			// We aim to have a maximum of 2 tasks, one already working, and one waiting in the queue
			// This way we can call this function as many times as needed e.g. because of Steam events
			lock (TradesSemaphore) {
				if (ParsingScheduled) {
					return;
				}

				ParsingScheduled = true;
			}

			await TradesSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (TradesSemaphore) {
					ParsingScheduled = false;
				}

				await ParseActiveTrades().ConfigureAwait(false);
			} finally {
				TradesSemaphore.Release();
			}
		}

		private static Dictionary<(uint AppID, Steam.Asset.EType Type), List<uint>> GetInventorySets(IReadOnlyCollection<Steam.Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(inventory));
				return null;
			}

			Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> sets = new Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>>();

			foreach (Steam.Asset item in inventory) {
				if (sets.TryGetValue((item.RealAppID, item.Type), out Dictionary<ulong, uint> set)) {
					if (set.TryGetValue(item.ClassID, out uint amount)) {
						set[item.ClassID] = amount + item.Amount;
					} else {
						set[item.ClassID] = item.Amount;
					}
				} else {
					sets[(item.RealAppID, item.Type)] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}
			}

			return sets.ToDictionary(set => set.Key, set => set.Value.Values.OrderByDescending(amount => amount).ToList());
		}

		private static bool IsTradeNeutralOrBetter(HashSet<Steam.Asset> inventory, IReadOnlyCollection<Steam.Asset> itemsToGive, IReadOnlyCollection<Steam.Asset> itemsToReceive) {
			if ((inventory == null) || (inventory.Count == 0) || (itemsToGive == null) || (itemsToGive.Count == 0) || (itemsToReceive == null) || (itemsToReceive.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(inventory) + " || " + nameof(itemsToGive) + " || " + nameof(itemsToReceive));
				return false;
			}

			// Input of this function is items we're expected to give/receive and our inventory (limited to realAppIDs of itemsToGive/itemsToReceive)
			// The objective is to determine whether the new state is beneficial (or at least neutral) towards us
			// There are a lot of factors involved here - different realAppIDs, different item types, possibility of user overpaying and more
			// All of those cases should be verified by our unit tests to ensure that the logic here matches all possible cases, especially those that were incorrectly handled previously

			// Firstly we get initial sets state of our inventory
			Dictionary<(uint AppID, Steam.Asset.EType Type), List<uint>> initialSets = GetInventorySets(inventory);

			// Once we have initial state, we remove items that we're supposed to give from our inventory
			// This loop is a bit more complex due to the fact that we might have a mix of the same item splitted into different amounts
			foreach (Steam.Asset itemToGive in itemsToGive) {
				uint amountToGive = itemToGive.Amount;
				HashSet<Steam.Asset> itemsToRemove = new HashSet<Steam.Asset>();

				// Keep in mind that ClassID is unique only within appID/contextID scope - we can do it like this because we're not dealing with non-Steam items here (otherwise we'd need to check appID and contextID too)
				foreach (Steam.Asset item in inventory.Where(item => item.ClassID == itemToGive.ClassID)) {
					if (amountToGive >= item.Amount) {
						itemsToRemove.Add(item);
						amountToGive -= item.Amount;
					} else {
						item.Amount -= amountToGive;
						amountToGive = 0;
					}

					if (amountToGive == 0) {
						break;
					}
				}

				if (amountToGive > 0) {
					ASF.ArchiLogger.LogNullError(nameof(amountToGive));
					return false;
				}

				if (itemsToRemove.Count > 0) {
					inventory.ExceptWith(itemsToRemove);
				}
			}

			// Now we can add items that we're supposed to receive, this one doesn't require advanced amounts logic since we can just add items regardless
			foreach (Steam.Asset itemToReceive in itemsToReceive) {
				inventory.Add(itemToReceive);
			}

			// Now we can get final sets state of our inventory after the exchange
			Dictionary<(uint AppID, Steam.Asset.EType Type), List<uint>> finalSets = GetInventorySets(inventory);

			// Once we have both states, we can check overall fairness
			foreach (KeyValuePair<(uint AppID, Steam.Asset.EType Type), List<uint>> finalSet in finalSets) {
				List<uint> beforeAmounts = initialSets[finalSet.Key];
				List<uint> afterAmounts = finalSet.Value;

				// If amount of unique items in the set decreases, this is always a bad trade (e.g. 1 1 -> 0 2)
				if (afterAmounts.Count < beforeAmounts.Count) {
					return false;
				}

				// If amount of unique items in the set increases, this is always a good trade (e.g. 0 2 -> 1 1)
				if (afterAmounts.Count > beforeAmounts.Count) {
					continue;
				}

				// At this point we're sure that amount of unique items stays the same, so we can evaluate actual sets
				// We make use of the fact that our amounts are already sorted in descending order, so we can just take the last value instead of calculating ourselves
				uint beforeSets = beforeAmounts[beforeAmounts.Count - 1];
				uint afterSets = afterAmounts[afterAmounts.Count - 1];

				// If amount of our sets for this game decreases, this is always a bad trade (e.g. 2 2 2 -> 3 2 1)
				if (afterSets < beforeSets) {
					return false;
				}

				// If amount of our sets for this game increases, this is always a good trade (e.g. 3 2 1 -> 2 2 2)
				if (afterSets > beforeSets) {
					continue;
				}

				// At this point we're sure that both number of unique items in the set stays the same, as well as number of our actual sets
				// We need to ensure set progress here, so we'll check if no final amount of a single item is lower than initial one
				// We also need to remember about overpaying, so we'll compare only appropriate indexes from a list (that is already sorted in descending order)
				for (byte i = 0; i < afterAmounts.Count; i++) {
					if (afterAmounts[i] < beforeAmounts[i]) {
						return false;
					}
				}
			}

			// If we didn't find any reason above to reject this trade, it's at least neutral+ for us - it increases our progress towards badge completion
			return true;
		}

		private async Task ParseActiveTrades() {
			HashSet<Steam.TradeOffer> tradeOffers = await Bot.ArchiWebHandler.GetActiveTradeOffers(IgnoredTrades).ConfigureAwait(false);
			if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
				return;
			}

			IEnumerable<Task<ParseTradeResult>> tasks = tradeOffers.Select(ParseTrade);
			ICollection<ParseTradeResult> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<ParseTradeResult>(tradeOffers.Count);
					foreach (Task<ParseTradeResult> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			if (Bot.HasMobileAuthenticator) {
				HashSet<ulong> acceptedWithItemLoseTradeIDs = results.Where(result => (result != null) && (result.Result == ParseTradeResult.EResult.AcceptedWithItemLose)).Select(result => result.TradeID).ToHashSet();
				if (acceptedWithItemLoseTradeIDs.Count > 0) {
					// Give Steam network some time to generate confirmations
					await Task.Delay(3000).ConfigureAwait(false);
					await Bot.AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, 0, acceptedWithItemLoseTradeIDs).ConfigureAwait(false);
				}
			}

			if (results.Any(result => (result != null) && ((result.Result == ParseTradeResult.EResult.AcceptedWithItemLose) || (result.Result == ParseTradeResult.EResult.AcceptedWithoutItemLose)))) {
				// If we finished a trade, perform a loot if user wants to do so
				await Bot.LootIfNeeded().ConfigureAwait(false);
			}
		}

		private async Task<ParseTradeResult> ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Bot.ArchiLogger.LogNullError(nameof(tradeOffer));
				return null;
			}

			if (tradeOffer.State != Steam.TradeOffer.ETradeOfferState.Active) {
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, tradeOffer.State));
				return null;
			}

			ParseTradeResult result = await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false);
			if (result == null) {
				Bot.ArchiLogger.LogNullError(nameof(result));
				return null;
			}

			switch (result.Result) {
				case ParseTradeResult.EResult.AcceptedWithItemLose:
				case ParseTradeResult.EResult.AcceptedWithoutItemLose:
					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.AcceptingTrade, tradeOffer.TradeOfferID));

					if (await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false)) {
						if (tradeOffer.ItemsToReceive.Sum(item => item.Amount) > tradeOffer.ItemsToGive.Sum(item => item.Amount)) {
							Bot.ArchiLogger.LogGenericTrace(string.Format(Strings.BotAcceptedDonationTrade, tradeOffer.TradeOfferID));
						}
					}

					break;
				case ParseTradeResult.EResult.RejectedPermanently:
				case ParseTradeResult.EResult.RejectedTemporarily:
					if (result.Result == ParseTradeResult.EResult.RejectedPermanently) {
						if (Bot.BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidTrades)) {
							goto case ParseTradeResult.EResult.RejectedAndBlacklisted;
						}

						IgnoredTrades.Add(tradeOffer.TradeOfferID);
					}

					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.IgnoringTrade, tradeOffer.TradeOfferID));
					break;
				case ParseTradeResult.EResult.RejectedAndBlacklisted:
					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.RejectingTrade, tradeOffer.TradeOfferID));
					await Bot.ArchiWebHandler.DeclineTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);
					break;
				default:
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.Result), result.Result));
					return null;
			}

			return result;
		}

		private async Task<ParseTradeResult> ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Bot.ArchiLogger.LogNullError(nameof(tradeOffer));
				return null;
			}

			if (tradeOffer.OtherSteamID64 != 0) {
				// Always accept trades from SteamMasterID
				if (Bot.IsMaster(tradeOffer.OtherSteamID64)) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, tradeOffer.ItemsToGive.Count > 0 ? ParseTradeResult.EResult.AcceptedWithItemLose : ParseTradeResult.EResult.AcceptedWithoutItemLose);
				}

				// Always deny trades from blacklisted steamIDs
				if (Bot.IsBlacklistedFromTrades(tradeOffer.OtherSteamID64)) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedAndBlacklisted);
				}
			}

			// Check if it's donation trade
			switch (tradeOffer.ItemsToGive.Count) {
				case 0 when tradeOffer.ItemsToReceive.Count == 0:
					// If it's steam fuckup, temporarily ignore it
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
				case 0:
					// Otherwise react accordingly, depending on our preference
					bool acceptDonations = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.AcceptDonations);
					bool acceptBotTrades = !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.DontAcceptBotTrades);

					// If we accept donations and bot trades, accept it right away
					if (acceptDonations && acceptBotTrades) {
						return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.AcceptedWithoutItemLose);
					}

					// If we don't accept donations, neither bot trades, deny it right away
					if (!acceptDonations && !acceptBotTrades) {
						return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
					}

					// Otherwise we either accept donations but not bot trades, or we accept bot trades but not donations
					bool isBotTrade = (tradeOffer.OtherSteamID64 != 0) && Bot.Bots.Values.Any(bot => bot.CachedSteamID == tradeOffer.OtherSteamID64);
					return new ParseTradeResult(tradeOffer.TradeOfferID, (acceptDonations && !isBotTrade) || (acceptBotTrades && isBotTrade) ? ParseTradeResult.EResult.AcceptedWithoutItemLose : ParseTradeResult.EResult.RejectedPermanently);
			}

			// If we don't have SteamTradeMatcher enabled, this is the end for us
			if (!Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
			}

			// Decline trade if we're giving more count-wise
			if (tradeOffer.ItemsToGive.Count > tradeOffer.ItemsToReceive.Count) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
			}

			// Decline trade if we're requested to handle any not-accepted item type or if it's not fair games/types exchange
			if (!tradeOffer.IsValidSteamItemsRequest(Bot.BotConfig.MatchableTypes) || !tradeOffer.IsFairTypesExchange()) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
			}

			// At this point we're sure that STM trade is valid

			// Fetch trade hold duration
			byte? holdDuration = await Bot.GetTradeHoldDuration(tradeOffer.OtherSteamID64, tradeOffer.TradeOfferID).ConfigureAwait(false);
			if (!holdDuration.HasValue) {
				// If we can't get trade hold duration, reject trade temporarily
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
			}

			// If user has a trade hold, we add extra logic
			if (holdDuration.Value > 0) {
				// If trade hold duration exceeds our max, or user asks for cards with short lifespan, reject the trade
				if ((holdDuration.Value > Program.GlobalConfig.MaxTradeHoldDuration) || tradeOffer.ItemsToGive.Any(item => ((item.Type == Steam.Asset.EType.FoilTradingCard) || (item.Type == Steam.Asset.EType.TradingCard)) && GlobalConfig.SalesBlacklist.Contains(item.RealAppID))) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedPermanently);
				}
			}

			// If we're matching everything, this is enough for us
			if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.AcceptedWithItemLose);
			}

			// Get appIDs/types we're interested in
			HashSet<uint> appIDs = new HashSet<uint>();
			HashSet<Steam.Asset.EType> types = new HashSet<Steam.Asset.EType>();

			foreach (Steam.Asset item in tradeOffer.ItemsToGive) {
				appIDs.Add(item.RealAppID);
				types.Add(item.Type);
			}

			// Now check if it's worth for us to do the trade
			HashSet<Steam.Asset> inventory = await Bot.ArchiWebHandler.GetInventory(Bot.CachedSteamID, wantedTypes: types, wantedRealAppIDs: appIDs).ConfigureAwait(false);
			if ((inventory == null) || (inventory.Count == 0)) {
				// If we can't check our inventory when not using MatchEverything, this is a temporary failure
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.RejectedTemporarily);
			}

			bool accept = IsTradeNeutralOrBetter(inventory, tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive);

			// Even if trade is not neutral+ for us right now, it might be in the future, unless we're bot account where we assume that inventory doesn't change
			return new ParseTradeResult(tradeOffer.TradeOfferID, accept ? ParseTradeResult.EResult.AcceptedWithItemLose : (Bot.BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidTrades) ? ParseTradeResult.EResult.RejectedPermanently : ParseTradeResult.EResult.RejectedTemporarily));
		}

		private sealed class ParseTradeResult {
			internal readonly EResult Result;

			internal readonly ulong TradeID;

			internal ParseTradeResult(ulong tradeID, EResult result) {
				if ((tradeID == 0) || (result == EResult.Unknown)) {
					throw new ArgumentNullException(nameof(tradeID) + " || " + nameof(result));
				}

				TradeID = tradeID;
				Result = result;
			}

			internal enum EResult : byte {
				Unknown,
				AcceptedWithItemLose,
				AcceptedWithoutItemLose,
				RejectedTemporarily,
				RejectedPermanently,
				RejectedAndBlacklisted
			}
		}
	}
}
