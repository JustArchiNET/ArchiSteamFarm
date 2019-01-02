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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Json;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal sealed class Trading : IDisposable {
		internal const byte MaxItemsPerTrade = byte.MaxValue; // This is due to limit on POST size in WebBrowser
		internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<ulong> HandledTradeOfferIDs = new ConcurrentHashSet<ulong>();
		private readonly SemaphoreSlim TradesSemaphore = new SemaphoreSlim(1, 1);

		private bool ParsingScheduled;

		internal Trading(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		public void Dispose() => TradesSemaphore.Dispose();

		internal static (Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> FullState, Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> TradableState) GetDividedInventoryState(IReadOnlyCollection<Steam.Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(inventory));

				return (null, null);
			}

			Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> fullState = new Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>>();
			Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> tradableState = new Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>>();

			foreach (Steam.Asset item in inventory) {
				(uint RealAppID, Steam.Asset.EType Type) key = (item.RealAppID, item.Type);

				if (fullState.TryGetValue(key, out Dictionary<ulong, uint> fullSet)) {
					if (fullSet.TryGetValue(item.ClassID, out uint amount)) {
						fullSet[item.ClassID] = amount + item.Amount;
					} else {
						fullSet[item.ClassID] = item.Amount;
					}
				} else {
					fullState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}

				if (!item.Tradable) {
					continue;
				}

				if (tradableState.TryGetValue(key, out Dictionary<ulong, uint> tradableSet)) {
					if (tradableSet.TryGetValue(item.ClassID, out uint amount)) {
						tradableSet[item.ClassID] = amount + item.Amount;
					} else {
						tradableSet[item.ClassID] = item.Amount;
					}
				} else {
					tradableState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}
			}

			return (fullState, tradableState);
		}

		internal static Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> GetInventoryState(IReadOnlyCollection<Steam.Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(inventory));

				return null;
			}

			Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> state = new Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>>();

			foreach (Steam.Asset item in inventory) {
				(uint RealAppID, Steam.Asset.EType Type) key = (item.RealAppID, item.Type);

				if (state.TryGetValue(key, out Dictionary<ulong, uint> set)) {
					if (set.TryGetValue(item.ClassID, out uint amount)) {
						set[item.ClassID] = amount + item.Amount;
					} else {
						set[item.ClassID] = item.Amount;
					}
				} else {
					state[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}
			}

			return state;
		}

		internal static HashSet<Steam.Asset> GetTradableItemsFromInventory(IReadOnlyCollection<Steam.Asset> inventory, IDictionary<ulong, uint> classIDs) {
			if ((inventory == null) || (inventory.Count == 0) || (classIDs == null) || (classIDs.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(inventory) + " || " + nameof(classIDs));

				return null;
			}

			HashSet<Steam.Asset> result = new HashSet<Steam.Asset>();

			foreach (Steam.Asset item in inventory.Where(item => item.Tradable)) {
				if (!classIDs.TryGetValue(item.ClassID, out uint amount)) {
					continue;
				}

				if (amount < item.Amount) {
					item.Amount = amount;
				}

				result.Add(item);

				if (amount == item.Amount) {
					classIDs.Remove(item.ClassID);
				} else {
					classIDs[item.ClassID] = amount - item.Amount;
				}
			}

			return result;
		}

		internal static bool IsEmptyForMatching(IReadOnlyDictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> fullState, IReadOnlyDictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> tradableState) {
			if ((fullState == null) || (tradableState == null)) {
				ASF.ArchiLogger.LogNullError(nameof(fullState) + " || " + nameof(tradableState));

				return false;
			}

			foreach (KeyValuePair<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> tradableSet in tradableState) {
				if (!fullState.TryGetValue(tradableSet.Key, out Dictionary<ulong, uint> fullSet) || (fullSet == null) || (fullSet.Count == 0)) {
					ASF.ArchiLogger.LogNullError(nameof(fullSet));

					return false;
				}

				if (!IsEmptyForMatching(fullSet, tradableSet.Value)) {
					return false;
				}
			}

			// We didn't find any matchable combinations, so this inventory is empty
			return true;
		}

		internal static bool IsEmptyForMatching(IReadOnlyDictionary<ulong, uint> fullSet, IReadOnlyDictionary<ulong, uint> tradableSet) {
			if ((fullSet == null) || (tradableSet == null)) {
				ASF.ArchiLogger.LogNullError(nameof(fullSet) + " || " + nameof(tradableSet));

				return false;
			}

			foreach (KeyValuePair<ulong, uint> tradableItem in tradableSet) {
				switch (tradableItem.Value) {
					case 0:

						// No tradable items, this should never happen, dictionary should not have this key to begin with
						ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(tradableItem.Value), tradableItem.Value));

						return false;
					case 1:

						// Single tradable item, can be matchable or not depending on the rest of the inventory
						if (!fullSet.TryGetValue(tradableItem.Key, out uint fullAmount) || (fullAmount == 0) || (fullAmount < tradableItem.Value)) {
							ASF.ArchiLogger.LogNullError(nameof(fullAmount));

							return false;
						}

						if (fullAmount > 1) {
							// If we have a single tradable item but more than 1 in total, this is matchable
							return false;
						}

						// A single exclusive tradable item is not matchable, continue
						continue;
					default:

						// Any other combination of tradable items is always matchable
						return false;
				}
			}

			// We didn't find any matchable combinations, so this inventory is empty
			return true;
		}

		internal static bool IsFairTypesExchange(IReadOnlyCollection<Steam.Asset> itemsToGive, IReadOnlyCollection<Steam.Asset> itemsToReceive) {
			if ((itemsToGive == null) || (itemsToGive.Count == 0) || (itemsToReceive == null) || (itemsToReceive.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(itemsToGive) + " || " + nameof(itemsToReceive));

				return false;
			}

			Dictionary<uint, Dictionary<Steam.Asset.EType, uint>> itemsToGivePerGame = new Dictionary<uint, Dictionary<Steam.Asset.EType, uint>>();

			foreach (Steam.Asset item in itemsToGive) {
				if (itemsToGivePerGame.TryGetValue(item.RealAppID, out Dictionary<Steam.Asset.EType, uint> itemsPerType)) {
					itemsPerType[item.Type] = itemsPerType.TryGetValue(item.Type, out uint amount) ? amount + item.Amount : item.Amount;
				} else {
					itemsPerType = new Dictionary<Steam.Asset.EType, uint> { [item.Type] = item.Amount };
					itemsToGivePerGame[item.RealAppID] = itemsPerType;
				}
			}

			Dictionary<uint, Dictionary<Steam.Asset.EType, uint>> itemsToReceivePerGame = new Dictionary<uint, Dictionary<Steam.Asset.EType, uint>>();

			foreach (Steam.Asset item in itemsToReceive) {
				if (itemsToReceivePerGame.TryGetValue(item.RealAppID, out Dictionary<Steam.Asset.EType, uint> itemsPerType)) {
					itemsPerType[item.Type] = itemsPerType.TryGetValue(item.Type, out uint amount) ? amount + item.Amount : item.Amount;
				} else {
					itemsPerType = new Dictionary<Steam.Asset.EType, uint> { [item.Type] = item.Amount };
					itemsToReceivePerGame[item.RealAppID] = itemsPerType;
				}
			}

			// Ensure that amount of items to give is at least amount of items to receive (per game and per type)
			foreach (KeyValuePair<uint, Dictionary<Steam.Asset.EType, uint>> itemsPerGame in itemsToGivePerGame) {
				if (!itemsToReceivePerGame.TryGetValue(itemsPerGame.Key, out Dictionary<Steam.Asset.EType, uint> otherItemsPerType)) {
					return false;
				}

				foreach (KeyValuePair<Steam.Asset.EType, uint> itemsPerType in itemsPerGame.Value) {
					if (!otherItemsPerType.TryGetValue(itemsPerType.Key, out uint otherAmount)) {
						return false;
					}

					if (itemsPerType.Value > otherAmount) {
						return false;
					}
				}
			}

			return true;
		}

		internal void OnDisconnected() => HandledTradeOfferIDs.Clear();

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

				using (await Bot.Actions.GetTradingLock().ConfigureAwait(false)) {
					await ParseActiveTrades().ConfigureAwait(false);
				}
			} finally {
				TradesSemaphore.Release();
			}
		}

		private static Dictionary<(uint AppID, Steam.Asset.EType Type), List<uint>> GetInventorySets(IReadOnlyCollection<Steam.Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(inventory));

				return null;
			}

			Dictionary<(uint AppID, Steam.Asset.EType Type), Dictionary<ulong, uint>> sets = GetInventoryState(inventory);

			return sets.ToDictionary(set => set.Key, set => set.Value.Values.OrderBy(amount => amount).ToList());
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
				// We make use of the fact that our amounts are already sorted in ascending order, so we can just take the first value instead of calculating ourselves
				uint beforeSets = beforeAmounts[0];
				uint afterSets = afterAmounts[0];

				// If amount of our sets for this game decreases, this is always a bad trade (e.g. 2 2 2 -> 3 2 1)
				if (afterSets < beforeSets) {
					return false;
				}

				// If amount of our sets for this game increases, this is always a good trade (e.g. 3 2 1 -> 2 2 2)
				if (afterSets > beforeSets) {
					continue;
				}

				// At this point we're sure that both number of unique items in the set stays the same, as well as number of our actual sets
				// We need to ensure set progress here and keep in mind overpaying, so we'll calculate neutrality as a difference in amounts at appropriate indexes
				// Neutrality can't reach value below 0 at any single point of calculation, as that would imply a loss of progress even if we'd end up with a positive value by the end
				int neutrality = 0;

				for (byte i = 0; i < afterAmounts.Count; i++) {
					neutrality += (int) (afterAmounts[i] - beforeAmounts[i]);

					if (neutrality < 0) {
						return false;
					}
				}
			}

			// If we didn't find any reason above to reject this trade, it's at least neutral+ for us - it increases our progress towards badge completion
			return true;
		}

		private async Task ParseActiveTrades() {
			HashSet<Steam.TradeOffer> tradeOffers = await Bot.ArchiWebHandler.GetActiveTradeOffers().ConfigureAwait(false);

			if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
				return;
			}

			if (HandledTradeOfferIDs.Count > 0) {
				HandledTradeOfferIDs.IntersectWith(tradeOffers.Select(tradeOffer => tradeOffer.TradeOfferID));
			}

			IEnumerable<Task<(ParseTradeResult TradeResult, bool RequiresMobileConfirmation)>> tasks = tradeOffers.Where(tradeOffer => !HandledTradeOfferIDs.Contains(tradeOffer.TradeOfferID)).Select(ParseTrade);
			IList<(ParseTradeResult TradeResult, bool RequiresMobileConfirmation)> results = await Utilities.InParallel(tasks).ConfigureAwait(false);

			if (Bot.HasMobileAuthenticator) {
				HashSet<ulong> mobileTradeOfferIDs = results.Where(result => (result.TradeResult != null) && (result.TradeResult.Result == ParseTradeResult.EResult.Accepted) && result.RequiresMobileConfirmation).Select(result => result.TradeResult.TradeOfferID).ToHashSet();

				if (mobileTradeOfferIDs.Count > 0) {
					if (!await Bot.Actions.AcceptConfirmations(true, Steam.ConfirmationDetails.EType.Trade, mobileTradeOfferIDs, true).ConfigureAwait(false)) {
						HandledTradeOfferIDs.ExceptWith(mobileTradeOfferIDs);

						return;
					}
				}
			}

			if (results.Any(result => (result.TradeResult != null) && (result.TradeResult.Result == ParseTradeResult.EResult.Accepted) && (!result.RequiresMobileConfirmation || Bot.HasMobileAuthenticator) && (result.TradeResult.ReceivingItemTypes?.Any(receivedItemType => Bot.BotConfig.LootableTypes.Contains(receivedItemType)) == true)) && Bot.BotConfig.SendOnFarmingFinished) {
				// If we finished a trade, perform a loot if user wants to do so
				await Bot.Actions.SendTradeOffer(wantedTypes: Bot.BotConfig.LootableTypes).ConfigureAwait(false);
			}
		}

		private async Task<(ParseTradeResult TradeResult, bool RequiresMobileConfirmation)> ParseTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Bot.ArchiLogger.LogNullError(nameof(tradeOffer));

				return (null, false);
			}

			if (tradeOffer.State != Steam.TradeOffer.ETradeOfferState.Active) {
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.ErrorIsInvalid, tradeOffer.State));

				return (null, false);
			}

			if (!HandledTradeOfferIDs.Add(tradeOffer.TradeOfferID)) {
				// We've already seen this trade, this should not happen
				Bot.ArchiLogger.LogGenericError(string.Format(Strings.IgnoringTrade, tradeOffer.TradeOfferID));

				return (new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Ignored, tradeOffer.ItemsToReceive), false);
			}

			ParseTradeResult result = await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false);

			if (result == null) {
				Bot.ArchiLogger.LogNullError(nameof(result));

				return (null, false);
			}

			switch (result.Result) {
				case ParseTradeResult.EResult.Accepted:
					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.AcceptingTrade, tradeOffer.TradeOfferID));

					(bool success, bool requiresMobileConfirmation) = await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);

					if (!success) {
						result.Result = ParseTradeResult.EResult.TryAgain;

						goto case ParseTradeResult.EResult.TryAgain;
					}

					if (tradeOffer.ItemsToReceive.Sum(item => item.Amount) > tradeOffer.ItemsToGive.Sum(item => item.Amount)) {
						Bot.ArchiLogger.LogGenericTrace(string.Format(Strings.BotAcceptedDonationTrade, tradeOffer.TradeOfferID));
					}

					return (result, requiresMobileConfirmation);
				case ParseTradeResult.EResult.Blacklisted:
				case ParseTradeResult.EResult.Rejected when Bot.BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidTrades):
					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.RejectingTrade, tradeOffer.TradeOfferID));

					if (!await Bot.ArchiWebHandler.DeclineTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false)) {
						result.Result = ParseTradeResult.EResult.TryAgain;

						goto case ParseTradeResult.EResult.TryAgain;
					}

					return (result, false);
				case ParseTradeResult.EResult.Ignored:
				case ParseTradeResult.EResult.Rejected:
					Bot.ArchiLogger.LogGenericInfo(string.Format(Strings.IgnoringTrade, tradeOffer.TradeOfferID));

					return (result, false);
				case ParseTradeResult.EResult.TryAgain:
					HandledTradeOfferIDs.Remove(tradeOffer.TradeOfferID);

					goto case ParseTradeResult.EResult.Ignored;
				default:
					Bot.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(result.Result), result.Result));

					return (null, false);
			}
		}

		private async Task<ParseTradeResult> ShouldAcceptTrade(Steam.TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				Bot.ArchiLogger.LogNullError(nameof(tradeOffer));

				return null;
			}

			if (tradeOffer.OtherSteamID64 != 0) {
				// Always accept trades from SteamMasterID
				if (Bot.IsMaster(tradeOffer.OtherSteamID64)) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, tradeOffer.ItemsToReceive);
				}

				// Always deny trades from blacklisted steamIDs
				if (Bot.IsBlacklistedFromTrades(tradeOffer.OtherSteamID64)) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Blacklisted, tradeOffer.ItemsToReceive);
				}
			}

			// Check if it's donation trade
			switch (tradeOffer.ItemsToGive.Count) {
				case 0 when tradeOffer.ItemsToReceive.Count == 0:

					// If it's steam issue, try again later
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, tradeOffer.ItemsToReceive);
				case 0:

					// Otherwise react accordingly, depending on our preference
					bool acceptDonations = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.AcceptDonations);
					bool acceptBotTrades = !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.DontAcceptBotTrades);

					// If we accept donations and bot trades, accept it right away
					if (acceptDonations && acceptBotTrades) {
						return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, tradeOffer.ItemsToReceive);
					}

					// If we don't accept donations, neither bot trades, deny it right away
					if (!acceptDonations && !acceptBotTrades) {
						return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, tradeOffer.ItemsToReceive);
					}

					// Otherwise we either accept donations but not bot trades, or we accept bot trades but not donations
					bool isBotTrade = (tradeOffer.OtherSteamID64 != 0) && Bot.Bots.Values.Any(bot => bot.SteamID == tradeOffer.OtherSteamID64);

					return new ParseTradeResult(tradeOffer.TradeOfferID, (acceptDonations && !isBotTrade) || (acceptBotTrades && isBotTrade) ? ParseTradeResult.EResult.Accepted : ParseTradeResult.EResult.Rejected, tradeOffer.ItemsToReceive);
			}

			// If we don't have SteamTradeMatcher enabled, this is the end for us
			if (!Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, tradeOffer.ItemsToReceive);
			}

			// Decline trade if we're giving more count-wise, this is a very naive pre-check, it'll be strengthened in more detailed fair types exchange next
			if (tradeOffer.ItemsToGive.Count > tradeOffer.ItemsToReceive.Count) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, tradeOffer.ItemsToReceive);
			}

			// Decline trade if we're requested to handle any not-accepted item type or if it's not fair games/types exchange
			if (!tradeOffer.IsValidSteamItemsRequest(Bot.BotConfig.MatchableTypes) || !IsFairTypesExchange(tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, tradeOffer.ItemsToReceive);
			}

			// At this point we're sure that STM trade is valid

			// Fetch trade hold duration
			byte? holdDuration = await Bot.GetTradeHoldDuration(tradeOffer.OtherSteamID64, tradeOffer.TradeOfferID).ConfigureAwait(false);

			if (!holdDuration.HasValue) {
				// If we can't get trade hold duration, try again later
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, tradeOffer.ItemsToReceive);
			}

			// If user has a trade hold, we add extra logic
			if (holdDuration.Value > 0) {
				// If trade hold duration exceeds our max, or user asks for cards with short lifespan, reject the trade
				if ((holdDuration.Value > Program.GlobalConfig.MaxTradeHoldDuration) || tradeOffer.ItemsToGive.Any(item => ((item.Type == Steam.Asset.EType.FoilTradingCard) || (item.Type == Steam.Asset.EType.TradingCard)) && CardsFarmer.SalesBlacklist.Contains(item.RealAppID))) {
					return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, tradeOffer.ItemsToReceive);
				}
			}

			// If we're matching everything, this is enough for us
			if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything)) {
				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, tradeOffer.ItemsToReceive);
			}

			// Get sets we're interested in
			HashSet<(uint AppID, Steam.Asset.EType Type)> wantedSets = new HashSet<(uint AppID, Steam.Asset.EType Type)>();

			foreach (Steam.Asset item in tradeOffer.ItemsToGive) {
				wantedSets.Add((item.RealAppID, item.Type));
			}

			// Now check if it's worth for us to do the trade
			HashSet<Steam.Asset> inventory = await Bot.ArchiWebHandler.GetInventory(Bot.SteamID, wantedSets: wantedSets).ConfigureAwait(false);

			if ((inventory == null) || (inventory.Count == 0)) {
				// If we can't check our inventory when not using MatchEverything, this is a temporary failure, try again later
				Bot.ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorIsEmpty, nameof(inventory)));

				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, tradeOffer.ItemsToReceive);
			}

			bool accept = IsTradeNeutralOrBetter(inventory, tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive);

			// We're now sure whether the trade is neutral+ for us or not
			return new ParseTradeResult(tradeOffer.TradeOfferID, accept ? ParseTradeResult.EResult.Accepted : ParseTradeResult.EResult.Rejected, tradeOffer.ItemsToReceive);
		}

		private sealed class ParseTradeResult {
			internal readonly HashSet<Steam.Asset.EType> ReceivingItemTypes;
			internal readonly ulong TradeOfferID;

			internal EResult Result { get; set; }

			internal ParseTradeResult(ulong tradeOfferID, EResult result, IReadOnlyCollection<Steam.Asset> itemsToReceive = null) {
				if ((tradeOfferID == 0) || (result == EResult.Unknown)) {
					throw new ArgumentNullException(nameof(tradeOfferID) + " || " + nameof(result));
				}

				TradeOfferID = tradeOfferID;
				Result = result;

				if ((itemsToReceive != null) && (itemsToReceive.Count > 0)) {
					ReceivingItemTypes = itemsToReceive.Select(item => item.Type).ToHashSet();
				}
			}

			internal enum EResult : byte {
				Unknown,
				Accepted,
				Blacklisted,
				Ignored,
				Rejected,
				TryAgain
			}
		}
	}
}
