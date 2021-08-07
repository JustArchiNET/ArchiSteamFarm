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
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Steam.Cards;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Security;
using ArchiSteamFarm.Steam.Storage;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Exchange {
	public sealed class Trading : IDisposable {
		internal const byte MaxItemsPerTrade = byte.MaxValue; // This is decided upon various factors, mainly stability of Steam servers when dealing with huge trade offers
		internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

		private readonly Bot Bot;
		private readonly ConcurrentHashSet<ulong> HandledTradeOfferIDs = new();
		private readonly SemaphoreSlim TradesSemaphore = new(1, 1);

		private bool ParsingScheduled;

		internal Trading(Bot bot) => Bot = bot ?? throw new ArgumentNullException(nameof(bot));

		public void Dispose() => TradesSemaphore.Dispose();

		[PublicAPI]
		public static Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), List<uint>> GetInventorySets(IReadOnlyCollection<Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				throw new ArgumentNullException(nameof(inventory));
			}

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> sets = GetInventoryState(inventory);

			return sets.ToDictionary(set => set.Key, set => set.Value.Values.OrderBy(amount => amount).ToList());
		}

		[PublicAPI]
		public static bool IsFairExchange(IReadOnlyCollection<Asset> itemsToGive, IReadOnlyCollection<Asset> itemsToReceive) {
			if ((itemsToGive == null) || (itemsToGive.Count == 0)) {
				throw new ArgumentNullException(nameof(itemsToGive));
			}

			if ((itemsToReceive == null) || (itemsToReceive.Count == 0)) {
				throw new ArgumentNullException(nameof(itemsToReceive));
			}

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), uint> itemsToGiveAmounts = new();

			foreach (Asset item in itemsToGive) {
				(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);
				itemsToGiveAmounts[key] = itemsToGiveAmounts.TryGetValue(key, out uint amount) ? amount + item.Amount : item.Amount;
			}

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), uint> itemsToReceiveAmounts = new();

			foreach (Asset item in itemsToReceive) {
				(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);
				itemsToReceiveAmounts[key] = itemsToReceiveAmounts.TryGetValue(key, out uint amount) ? amount + item.Amount : item.Amount;
			}

			// Ensure that amount of items to give is at least amount of items to receive (per all fairness factors)
			foreach (((uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) key, uint amountToGive) in itemsToGiveAmounts) {
				if (!itemsToReceiveAmounts.TryGetValue(key, out uint amountToReceive) || (amountToGive > amountToReceive)) {
					return false;
				}
			}

			return true;
		}

		[PublicAPI]
		public static bool IsTradeNeutralOrBetter(HashSet<Asset> inventory, IReadOnlyCollection<Asset> itemsToGive, IReadOnlyCollection<Asset> itemsToReceive) {
			if ((inventory == null) || (inventory.Count == 0)) {
				throw new ArgumentNullException(nameof(inventory));
			}

			if ((itemsToGive == null) || (itemsToGive.Count == 0)) {
				throw new ArgumentNullException(nameof(itemsToGive));
			}

			if ((itemsToReceive == null) || (itemsToReceive.Count == 0)) {
				throw new ArgumentNullException(nameof(itemsToReceive));
			}

			// Input of this function is items we're expected to give/receive and our inventory (limited to realAppIDs of itemsToGive/itemsToReceive)
			// The objective is to determine whether the new state is beneficial (or at least neutral) towards us
			// There are a lot of factors involved here - different realAppIDs, different item types, possibility of user overpaying and more
			// All of those cases should be verified by our unit tests to ensure that the logic here matches all possible cases, especially those that were incorrectly handled previously

			// Firstly we get initial sets state of our inventory
			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), List<uint>> initialSets = GetInventorySets(inventory);

			// Once we have initial state, we remove items that we're supposed to give from our inventory
			// This loop is a bit more complex due to the fact that we might have a mix of the same item splitted into different amounts
			foreach (Asset itemToGive in itemsToGive) {
				uint amountToGive = itemToGive.Amount;
				HashSet<Asset> itemsToRemove = new();

				// Keep in mind that ClassID is unique only within appID scope - we can do it like this because we're not dealing with non-Steam items here (otherwise we'd need to check appID too)
				foreach (Asset item in inventory.Where(item => item.ClassID == itemToGive.ClassID)) {
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
					throw new InvalidOperationException(nameof(amountToGive));
				}

				if (itemsToRemove.Count > 0) {
					inventory.ExceptWith(itemsToRemove);
				}
			}

			// Now we can add items that we're supposed to receive, this one doesn't require advanced amounts logic since we can just add items regardless
			foreach (Asset itemToReceive in itemsToReceive) {
				inventory.Add(itemToReceive);
			}

			// Now we can get final sets state of our inventory after the exchange
			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), List<uint>> finalSets = GetInventorySets(inventory);

			// Once we have both states, we can check overall fairness
			foreach (((uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) set, List<uint> beforeAmounts) in initialSets) {
				if (!finalSets.TryGetValue(set, out List<uint>? afterAmounts)) {
					// If we have no info about this set, then it has to be a bad one
					return false;
				}

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

		internal static (Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> FullState, Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> TradableState) GetDividedInventoryState(IReadOnlyCollection<Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				throw new ArgumentNullException(nameof(inventory));
			}

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> fullState = new();
			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> tradableState = new();

			foreach (Asset item in inventory) {
				(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

				if (fullState.TryGetValue(key, out Dictionary<ulong, uint>? fullSet)) {
					fullSet[item.ClassID] = fullSet.TryGetValue(item.ClassID, out uint amount) ? amount + item.Amount : item.Amount;
				} else {
					fullState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}

				if (!item.Tradable) {
					continue;
				}

				if (tradableState.TryGetValue(key, out Dictionary<ulong, uint>? tradableSet)) {
					tradableSet[item.ClassID] = tradableSet.TryGetValue(item.ClassID, out uint amount) ? amount + item.Amount : item.Amount;
				} else {
					tradableState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}
			}

			return (fullState, tradableState);
		}

		internal static Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> GetTradableInventoryState(IReadOnlyCollection<Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				throw new ArgumentNullException(nameof(inventory));
			}

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> tradableState = new();

			foreach (Asset item in inventory.Where(item => item.Tradable)) {
				(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

				if (tradableState.TryGetValue(key, out Dictionary<ulong, uint>? tradableSet)) {
					tradableSet[item.ClassID] = tradableSet.TryGetValue(item.ClassID, out uint amount) ? amount + item.Amount : item.Amount;
				} else {
					tradableState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}
			}

			return tradableState;
		}

		internal static HashSet<Asset> GetTradableItemsFromInventory(IReadOnlyCollection<Asset> inventory, IDictionary<ulong, uint> classIDs) {
			if ((inventory == null) || (inventory.Count == 0)) {
				throw new ArgumentNullException(nameof(inventory));
			}

			if ((classIDs == null) || (classIDs.Count == 0)) {
				throw new ArgumentNullException(nameof(classIDs));
			}

			HashSet<Asset> result = new();

			foreach (Asset item in inventory.Where(item => item.Tradable)) {
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

		internal static bool IsEmptyForMatching(IReadOnlyDictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> fullState, IReadOnlyDictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> tradableState) {
			if (fullState == null) {
				throw new ArgumentNullException(nameof(fullState));
			}

			if (tradableState == null) {
				throw new ArgumentNullException(nameof(tradableState));
			}

			foreach (((uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) set, IReadOnlyDictionary<ulong, uint> state) in tradableState) {
				if (!fullState.TryGetValue(set, out Dictionary<ulong, uint>? fullSet) || (fullSet.Count == 0)) {
					throw new InvalidOperationException(nameof(fullSet));
				}

				if (!IsEmptyForMatching(fullSet, state)) {
					return false;
				}
			}

			// We didn't find any matchable combinations, so this inventory is empty
			return true;
		}

		internal static bool IsEmptyForMatching(IReadOnlyDictionary<ulong, uint> fullSet, IReadOnlyDictionary<ulong, uint> tradableSet) {
			if (fullSet == null) {
				throw new ArgumentNullException(nameof(fullSet));
			}

			if (tradableSet == null) {
				throw new ArgumentNullException(nameof(tradableSet));
			}

			foreach ((ulong classID, uint amount) in tradableSet) {
				switch (amount) {
					case 0:
						// No tradable items, this should never happen, dictionary should not have this key to begin with
						throw new InvalidOperationException(nameof(amount));
					case 1:
						// Single tradable item, can be matchable or not depending on the rest of the inventory
						if (!fullSet.TryGetValue(classID, out uint fullAmount) || (fullAmount == 0) || (fullAmount < amount)) {
							throw new InvalidOperationException(nameof(fullAmount));
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
				bool lootableTypesReceived;

				using (await Bot.Actions.GetTradingLock().ConfigureAwait(false)) {
					lock (TradesSemaphore) {
						ParsingScheduled = false;
					}

					lootableTypesReceived = await ParseActiveTrades().ConfigureAwait(false);
				}

				if (lootableTypesReceived && Bot.BotConfig.SendOnFarmingFinished && (Bot.BotConfig.LootableTypes.Count > 0)) {
					await Bot.Actions.SendInventory(filterFunction: item => Bot.BotConfig.LootableTypes.Contains(item.Type)).ConfigureAwait(false);
				}
			} finally {
				TradesSemaphore.Release();
			}
		}

		private static Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> GetInventoryState(IReadOnlyCollection<Asset> inventory) {
			if ((inventory == null) || (inventory.Count == 0)) {
				throw new ArgumentNullException(nameof(inventory));
			}

			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), Dictionary<ulong, uint>> state = new();

			foreach (Asset item in inventory) {
				(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

				if (state.TryGetValue(key, out Dictionary<ulong, uint>? set)) {
					set[item.ClassID] = set.TryGetValue(item.ClassID, out uint amount) ? amount + item.Amount : item.Amount;
				} else {
					state[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
				}
			}

			return state;
		}

		private async Task<bool> ParseActiveTrades() {
			HashSet<TradeOffer>? tradeOffers = await Bot.ArchiWebHandler.GetActiveTradeOffers().ConfigureAwait(false);

			if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
				return false;
			}

			if (HandledTradeOfferIDs.Count > 0) {
				HandledTradeOfferIDs.IntersectWith(tradeOffers.Select(tradeOffer => tradeOffer.TradeOfferID));
			}

			IEnumerable<Task<(ParseTradeResult? TradeResult, bool RequiresMobileConfirmation)>> tasks = tradeOffers.Where(tradeOffer => !HandledTradeOfferIDs.Contains(tradeOffer.TradeOfferID)).Select(ParseTrade);
			IList<(ParseTradeResult? TradeResult, bool RequiresMobileConfirmation)> results = await Utilities.InParallel(tasks).ConfigureAwait(false);

			if (Bot.HasMobileAuthenticator) {
				HashSet<ulong> mobileTradeOfferIDs = results.Where(result => (result.TradeResult?.Result == ParseTradeResult.EResult.Accepted) && result.RequiresMobileConfirmation).Select(result => result.TradeResult!.TradeOfferID).ToHashSet();

				if (mobileTradeOfferIDs.Count > 0) {
					(bool twoFactorSuccess, _, _) = await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EType.Trade, mobileTradeOfferIDs, true).ConfigureAwait(false);

					if (!twoFactorSuccess) {
						HandledTradeOfferIDs.ExceptWith(mobileTradeOfferIDs);

						return false;
					}
				}
			}

			HashSet<ParseTradeResult> validTradeResults = results.Where(result => result.TradeResult != null).Select(result => result.TradeResult!).ToHashSet();

			if (validTradeResults.Count > 0) {
				await PluginsCore.OnBotTradeOfferResults(Bot, validTradeResults).ConfigureAwait(false);
			}

			return results.Any(result => (result.TradeResult?.Result == ParseTradeResult.EResult.Accepted) && (!result.RequiresMobileConfirmation || Bot.HasMobileAuthenticator) && (result.TradeResult.ReceivedItemTypes?.Any(receivedItemType => Bot.BotConfig.LootableTypes.Contains(receivedItemType)) == true));
		}

		private async Task<(ParseTradeResult? TradeResult, bool RequiresMobileConfirmation)> ParseTrade(TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				throw new ArgumentNullException(nameof(tradeOffer));
			}

			if (tradeOffer.State != ETradeOfferState.Active) {
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, tradeOffer.State));

				return (null, false);
			}

			if (!HandledTradeOfferIDs.Add(tradeOffer.TradeOfferID)) {
				// We've already seen this trade, this should not happen
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.IgnoringTrade, tradeOffer.TradeOfferID));

				return (new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Ignored, tradeOffer.ItemsToReceive), false);
			}

			ParseTradeResult.EResult result = await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false);
			bool tradeRequiresMobileConfirmation = false;

			switch (result) {
				case ParseTradeResult.EResult.Ignored:
				case ParseTradeResult.EResult.Rejected:
					bool accept = await PluginsCore.OnBotTradeOffer(Bot, tradeOffer).ConfigureAwait(false);

					if (accept) {
						result = ParseTradeResult.EResult.Accepted;
					}

					break;
			}

			switch (result) {
				case ParseTradeResult.EResult.Accepted:
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.AcceptingTrade, tradeOffer.TradeOfferID));

					(bool success, bool requiresMobileConfirmation) = await Bot.ArchiWebHandler.AcceptTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false);

					if (!success) {
						result = ParseTradeResult.EResult.TryAgain;

						goto case ParseTradeResult.EResult.TryAgain;
					}

					if (tradeOffer.ItemsToReceive.Sum(item => item.Amount) > tradeOffer.ItemsToGive.Sum(item => item.Amount)) {
						Bot.ArchiLogger.LogGenericTrace(string.Format(CultureInfo.CurrentCulture, Strings.BotAcceptedDonationTrade, tradeOffer.TradeOfferID));
					}

					tradeRequiresMobileConfirmation = requiresMobileConfirmation;

					break;
				case ParseTradeResult.EResult.Blacklisted:
				case ParseTradeResult.EResult.Rejected when Bot.BotConfig.BotBehaviour.HasFlag(BotConfig.EBotBehaviour.RejectInvalidTrades):
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.RejectingTrade, tradeOffer.TradeOfferID));

					if (!await Bot.ArchiWebHandler.DeclineTradeOffer(tradeOffer.TradeOfferID).ConfigureAwait(false)) {
						result = ParseTradeResult.EResult.TryAgain;

						goto case ParseTradeResult.EResult.TryAgain;
					}

					break;
				case ParseTradeResult.EResult.Ignored:
				case ParseTradeResult.EResult.Rejected:
					Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IgnoringTrade, tradeOffer.TradeOfferID));

					break;
				case ParseTradeResult.EResult.TryAgain:
					HandledTradeOfferIDs.Remove(tradeOffer.TradeOfferID);

					goto case ParseTradeResult.EResult.Ignored;
				default:
					Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(result), result));

					return (null, false);
			}

			return (new ParseTradeResult(tradeOffer.TradeOfferID, result, tradeOffer.ItemsToReceive), tradeRequiresMobileConfirmation);
		}

		private async Task<ParseTradeResult.EResult> ShouldAcceptTrade(TradeOffer tradeOffer) {
			if (tradeOffer == null) {
				throw new ArgumentNullException(nameof(tradeOffer));
			}

			if (ASF.GlobalConfig == null) {
				throw new InvalidOperationException(nameof(ASF.GlobalConfig));
			}

			if (Bot.Bots == null) {
				throw new InvalidOperationException(nameof(Bot.Bots));
			}

			if (tradeOffer.OtherSteamID64 != 0) {
				// Always accept trades from SteamMasterID
				if (Bot.HasAccess(tradeOffer.OtherSteamID64, BotConfig.EAccess.Master)) {
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, nameof(tradeOffer.OtherSteamID64) + " " + tradeOffer.OtherSteamID64 + ": " + BotConfig.EAccess.Master));

					return ParseTradeResult.EResult.Accepted;
				}

				// Always deny trades from blacklisted steamIDs
				if (Bot.IsBlacklistedFromTrades(tradeOffer.OtherSteamID64)) {
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Blacklisted, nameof(tradeOffer.OtherSteamID64) + " " + tradeOffer.OtherSteamID64));

					return ParseTradeResult.EResult.Blacklisted;
				}
			}

			// Check if it's donation trade
			switch (tradeOffer.ItemsToGive.Count) {
				case 0 when tradeOffer.ItemsToReceive.Count == 0:
					// If it's steam issue, try again later
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, nameof(tradeOffer.ItemsToReceive.Count) + " = 0"));

					return ParseTradeResult.EResult.TryAgain;
				case 0:
					// Otherwise react accordingly, depending on our preference
					bool acceptDonations = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.AcceptDonations);
					bool acceptBotTrades = !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.DontAcceptBotTrades);

					switch (acceptDonations) {
						case true when acceptBotTrades:
							// If we accept donations and bot trades, accept it right away
							Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, nameof(acceptDonations) + " = " + true + " && " + nameof(acceptBotTrades) + " = " + true));

							return ParseTradeResult.EResult.Accepted;

						case false when !acceptBotTrades:
							// If we don't accept donations, neither bot trades, deny it right away
							Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, nameof(acceptDonations) + " = " + false + " && " + nameof(acceptBotTrades) + " = " + false));

							return ParseTradeResult.EResult.Rejected;
					}

					// Otherwise we either accept donations but not bot trades, or we accept bot trades but not donations
					bool isBotTrade = (tradeOffer.OtherSteamID64 != 0) && Bot.Bots.Values.Any(bot => bot.SteamID == tradeOffer.OtherSteamID64);

					ParseTradeResult.EResult result = (acceptDonations && !isBotTrade) || (acceptBotTrades && isBotTrade) ? ParseTradeResult.EResult.Accepted : ParseTradeResult.EResult.Rejected;

					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, result, nameof(acceptDonations) + " = " + acceptDonations + " && " + nameof(acceptBotTrades) + " = " + acceptBotTrades + " && " + nameof(isBotTrade) + " = " + isBotTrade));

					return result;
			}

			// If we don't have SteamTradeMatcher enabled, this is the end for us
			if (!Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, nameof(BotConfig.ETradingPreferences.SteamTradeMatcher) + " = " + false));

				return ParseTradeResult.EResult.Rejected;
			}

			// Decline trade if we're giving more count-wise, this is a very naive pre-check, it'll be strengthened in more detailed fair types exchange next
			if (tradeOffer.ItemsToGive.Count > tradeOffer.ItemsToReceive.Count) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, nameof(tradeOffer.ItemsToGive.Count) + ": " + tradeOffer.ItemsToGive.Count + " > " + tradeOffer.ItemsToReceive.Count));

				return ParseTradeResult.EResult.Rejected;
			}

			// Decline trade if we're requested to handle any not-accepted item type or if it's not fair games/types exchange
			if (!tradeOffer.IsValidSteamItemsRequest(Bot.BotConfig.MatchableTypes) || !IsFairExchange(tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, nameof(tradeOffer.IsValidSteamItemsRequest) + " || " + nameof(IsFairExchange)));

				return ParseTradeResult.EResult.Rejected;
			}

			// At this point we're sure that STM trade is valid

			// Fetch trade hold duration
			byte? holdDuration = await Bot.GetTradeHoldDuration(tradeOffer.OtherSteamID64, tradeOffer.TradeOfferID).ConfigureAwait(false);

			switch (holdDuration) {
				case null:
					// If we can't get trade hold duration, try again later
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, nameof(holdDuration)));

					return ParseTradeResult.EResult.TryAgain;

				// If user has a trade hold, we add extra logic
				// If trade hold duration exceeds our max, or user asks for cards with short lifespan, reject the trade
				case > 0 when (holdDuration.Value > ASF.GlobalConfig.MaxTradeHoldDuration) || tradeOffer.ItemsToGive.Any(item => item.Type is Asset.EType.FoilTradingCard or Asset.EType.TradingCard && CardsFarmer.SalesBlacklist.Contains(item.RealAppID)):
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, nameof(holdDuration) + " > 0: " + holdDuration.Value));

					return ParseTradeResult.EResult.Rejected;
			}

			// If we're matching everything, this is enough for us
			if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, BotConfig.ETradingPreferences.MatchEverything));

				return ParseTradeResult.EResult.Accepted;
			}

			// Get sets we're interested in
			HashSet<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity)> wantedSets = new();

			foreach (Asset item in tradeOffer.ItemsToGive) {
				wantedSets.Add((item.RealAppID, item.Type, item.Rarity));
			}

			// Now check if it's worth for us to do the trade
			HashSet<Asset> inventory;

			try {
				inventory = await Bot.ArchiWebHandler.GetInventoryAsync().Where(item => wantedSets.Contains((item.RealAppID, item.Type, item.Rarity))).ToHashSetAsync().ConfigureAwait(false);
			} catch (HttpRequestException e) {
				// If we can't check our inventory when not using MatchEverything, this is a temporary failure, try again later
				Bot.ArchiLogger.LogGenericWarningException(e);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, nameof(inventory)));

				return ParseTradeResult.EResult.TryAgain;
			} catch (Exception e) {
				// If we can't check our inventory when not using MatchEverything, this is a temporary failure, try again later
				Bot.ArchiLogger.LogGenericException(e);
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, nameof(inventory)));

				return ParseTradeResult.EResult.TryAgain;
			}

			if (inventory.Count == 0) {
				// If we can't check our inventory when not using MatchEverything, this is a temporary failure, try again later
				Bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(inventory)));
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, nameof(inventory)));

				return ParseTradeResult.EResult.TryAgain;
			}

			bool accept = IsTradeNeutralOrBetter(inventory, tradeOffer.ItemsToGive.Select(item => item.CreateShallowCopy()).ToHashSet(), tradeOffer.ItemsToReceive.Select(item => item.CreateShallowCopy()).ToHashSet());

			// We're now sure whether the trade is neutral+ for us or not
			ParseTradeResult.EResult acceptResult = accept ? ParseTradeResult.EResult.Accepted : ParseTradeResult.EResult.Rejected;

			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, acceptResult, nameof(IsTradeNeutralOrBetter)));

			return acceptResult;
		}
	}
}
