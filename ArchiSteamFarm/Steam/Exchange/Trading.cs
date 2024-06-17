// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Steam.Cards;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Exchange;

public sealed class Trading : IDisposable {
	internal const byte MaxItemsPerTrade = byte.MaxValue; // This is decided upon various factors, mainly stability of Steam servers when dealing with huge trade offers
	internal const byte MaxTradesPerAccount = 5; // This is limit introduced by Valve

	private readonly Bot Bot;
	private readonly ConcurrentHashSet<ulong> HandledTradeOfferIDs = [];
	private readonly SemaphoreSlim TradesSemaphore = new(1, 1);

	private bool ParsingScheduled;

	internal Trading(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Bot = bot;
	}

	public void Dispose() => TradesSemaphore.Dispose();

	[PublicAPI]
	public static Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), List<uint>> GetInventorySets(IReadOnlyCollection<Asset> inventory) {
		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> sets = GetInventoryState(inventory);

		return sets.ToDictionary(static set => set.Key, static set => set.Value.Values.OrderBy(static amount => amount).ToList());
	}

	[PublicAPI]
	public static bool IsFairExchange(IReadOnlyCollection<Asset> itemsToGive, IReadOnlyCollection<Asset> itemsToReceive) {
		if ((itemsToGive == null) || (itemsToGive.Count == 0)) {
			throw new ArgumentNullException(nameof(itemsToGive));
		}

		if ((itemsToReceive == null) || (itemsToReceive.Count == 0)) {
			throw new ArgumentNullException(nameof(itemsToReceive));
		}

		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), uint> itemsToGiveAmounts = new();

		foreach (Asset item in itemsToGive) {
			(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);
			itemsToGiveAmounts[key] = itemsToGiveAmounts.GetValueOrDefault(key) + item.Amount;
		}

		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), uint> itemsToReceiveAmounts = new();

		foreach (Asset item in itemsToReceive) {
			(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);
			itemsToReceiveAmounts[key] = itemsToReceiveAmounts.GetValueOrDefault(key) + item.Amount;
		}

		// Ensure that amount of items to give is at least amount of items to receive (per all fairness factors)
		foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) key, uint amountToGive) in itemsToGiveAmounts) {
			if (!itemsToReceiveAmounts.TryGetValue(key, out uint amountToReceive) || (amountToGive > amountToReceive)) {
				return false;
			}
		}

		return true;
	}

	[PublicAPI]
	public static bool IsTradeNeutralOrBetter(IReadOnlyCollection<Asset> inventory, IReadOnlyCollection<Asset> itemsToGive, IReadOnlyCollection<Asset> itemsToReceive) {
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

		// We start from a deep copy of the inventory, along with its assets, since we'll be manipulating amounts in them
		HashSet<Asset> inventoryState = inventory.Select(static item => item.DeepClone()).ToHashSet();

		// Firstly we get initial sets state of our inventory
		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), List<uint>> initialSets = GetInventorySets(inventoryState);

		// Once we have initial state, we remove items that we're supposed to give from our inventory
		// This loop is a bit more complex due to the fact that we might have a mix of the same item splitted into different amounts
		HashSet<Asset> itemsToRemove = [];

		foreach (Asset itemToGive in itemsToGive) {
			uint amountToGive = itemToGive.Amount;

			foreach (Asset item in inventoryState.Where(item => (item.AppID == itemToGive.AppID) && (item.ClassID == itemToGive.ClassID) && (item.InstanceID == itemToGive.InstanceID))) {
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
				inventoryState.ExceptWith(itemsToRemove);

				itemsToRemove.Clear();
			}
		}

		// Now we can add items that we're supposed to receive, this one doesn't require advanced amounts logic since we can just add items regardless
		foreach (Asset itemToReceive in itemsToReceive) {
			inventoryState.Add(itemToReceive);
		}

		// Now we can get final sets state of our inventory after the exchange
		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), List<uint>> finalSets = GetInventorySets(inventoryState);

		// Once we have both states, we can check overall fairness
		foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) set, List<uint> beforeAmounts) in initialSets) {
			if (!finalSets.TryGetValue(set, out List<uint>? afterAmounts)) {
				// If we have no info about this set, then it has to be a bad one
				return false;
			}

			// If amount of unique items in the set decreases, this is always a bad trade (e.g. 1 1 -> 0 2)
			if (afterAmounts.Count < beforeAmounts.Count) {
				return false;
			}

			// Otherwise, fill the missing holes in our data if needed, since we actually had zeros there
			while (afterAmounts.Count > beforeAmounts.Count) {
				beforeAmounts.Insert(0, 0);
			}

			// Now we need to ensure set progress and keep in mind overpaying, so we'll calculate neutrality as a difference in amounts at appropriate indexes
			// We start from the amounts we have the least of, our data is already sorted in ascending order, so we can just subtract and compare until we cover every amount
			// Neutrality can't reach value below 0 at any single point of calculation, as that would imply a loss of progress even if we'd end up with a positive value by the end
			int neutrality = 0;

			for (byte i = 0; i < afterAmounts.Count; i++) {
				// We assume that the difference between amounts will be within int range, therefore we accept underflow here (for subtraction), and since we cast that result to int afterwards, we also accept overflow for the cast itself
				neutrality += unchecked((int) (afterAmounts[i] - beforeAmounts[i]));

				if (neutrality < 0) {
					return false;
				}
			}
		}

		// If we didn't find any reason above to reject this trade, it's at least neutral+ for us - it increases our progress towards badge completion
		return true;
	}

	internal void OnDisconnected() => HandledTradeOfferIDs.Clear();

	internal async Task OnNewTrade() {
		// We aim to have a maximum of 2 tasks, one already working, and one waiting in the queue
		// This way we can call this function as many times as needed e.g. because of Steam events

		// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
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
				// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
				lock (TradesSemaphore) {
					ParsingScheduled = false;
				}

				lootableTypesReceived = await ParseActiveTrades().ConfigureAwait(false);
			}

			if (lootableTypesReceived && Bot.BotConfig.FarmingPreferences.HasFlag(BotConfig.EFarmingPreferences.SendOnFarmingFinished) && (Bot.BotConfig.LootableTypes.Count > 0)) {
				await Bot.Actions.SendInventory(filterFunction: item => Bot.BotConfig.LootableTypes.Contains(item.Type)).ConfigureAwait(false);
			}
		} finally {
			TradesSemaphore.Release();
		}
	}

	private static Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> GetInventoryState(IReadOnlyCollection<Asset> inventory) {
		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> state = new();

		foreach (Asset item in inventory) {
			(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

			if (state.TryGetValue(key, out Dictionary<ulong, uint>? set)) {
				set[item.ClassID] = set.GetValueOrDefault(item.ClassID) + item.Amount;
			} else {
				state[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
			}
		}

		return state;
	}

	private async Task<bool> ParseActiveTrades() {
		HashSet<TradeOffer>? tradeOffers = await Bot.ArchiWebHandler.GetTradeOffers(true, true, false, true).ConfigureAwait(false);

		if ((tradeOffers == null) || (tradeOffers.Count == 0)) {
			return false;
		}

		if (HandledTradeOfferIDs.Count > 0) {
			HandledTradeOfferIDs.IntersectWith(tradeOffers.Select(static tradeOffer => tradeOffer.TradeOfferID));
		}

		IEnumerable<Task<ParseTradeResult>> tasks = tradeOffers.Where(tradeOffer => (tradeOffer.State == ETradeOfferState.Active) && HandledTradeOfferIDs.Add(tradeOffer.TradeOfferID)).Select(ParseTrade);
		IList<ParseTradeResult> results = await Utilities.InParallel(tasks).ConfigureAwait(false);

		if (Bot.HasMobileAuthenticator) {
			HashSet<ParseTradeResult> mobileTradeResults = results.Where(static result => result is { Result: ParseTradeResult.EResult.Accepted, Confirmed: false }).ToHashSet();

			if (mobileTradeResults.Count > 0) {
				HashSet<ulong> mobileTradeOfferIDs = mobileTradeResults.Select(static tradeOffer => tradeOffer.TradeOfferID).ToHashSet();

				(bool twoFactorSuccess, _, _) = await Bot.Actions.HandleTwoFactorAuthenticationConfirmations(true, Confirmation.EConfirmationType.Trade, mobileTradeOfferIDs, true).ConfigureAwait(false);

				if (twoFactorSuccess) {
					foreach (ParseTradeResult mobileTradeResult in mobileTradeResults) {
						mobileTradeResult.Confirmed = true;
					}
				} else {
					HandledTradeOfferIDs.ExceptWith(mobileTradeOfferIDs);
				}
			}
		}

		if (results.Count > 0) {
			await PluginsCore.OnBotTradeOfferResults(Bot, results as IReadOnlyCollection<ParseTradeResult> ?? results.ToHashSet()).ConfigureAwait(false);
		}

		return results.Any(result => result is { Result: ParseTradeResult.EResult.Accepted, Confirmed: true } && (result.ItemsToReceive?.Any(receivedItem => Bot.BotConfig.LootableTypes.Contains(receivedItem.Type)) == true));
	}

	private async Task<ParseTradeResult> ParseTrade(TradeOffer tradeOffer) {
		ArgumentNullException.ThrowIfNull(tradeOffer);

		ParseTradeResult.EResult result = await ShouldAcceptTrade(tradeOffer).ConfigureAwait(false);
		bool tradeRequiresMobileConfirmation = false;

		switch (result) {
			case ParseTradeResult.EResult.Blacklisted:
			case ParseTradeResult.EResult.Ignored:
			case ParseTradeResult.EResult.Rejected:
				bool accept = await PluginsCore.OnBotTradeOffer(Bot, tradeOffer, result).ConfigureAwait(false);

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

				// We do not expect to see this trade offer again, so retry it if needed
				HandledTradeOfferIDs.Remove(tradeOffer.TradeOfferID);

				if (tradeOffer.ItemsToReceive.Sum(static item => item.Amount) > tradeOffer.ItemsToGive.Sum(static item => item.Amount)) {
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

				// We do not expect to see this trade offer again, so retry it if needed
				HandledTradeOfferIDs.Remove(tradeOffer.TradeOfferID);

				break;
			case ParseTradeResult.EResult.Ignored:
			case ParseTradeResult.EResult.Rejected:
				// We expect to see this trade offer in the future, so we keep it in HandledTradeOfferIDs if it wasn't removed as part of other result
				Bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.IgnoringTrade, tradeOffer.TradeOfferID));

				break;
			case ParseTradeResult.EResult.TryAgain:
				// We expect to see this trade offer again and we intend to retry it
				HandledTradeOfferIDs.Remove(tradeOffer.TradeOfferID);

				goto case ParseTradeResult.EResult.Ignored;
			default:
				Bot.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(result), result));

				return new ParseTradeResult(tradeOffer.TradeOfferID, ParseTradeResult.EResult.Ignored, false, tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive);
		}

		return new ParseTradeResult(tradeOffer.TradeOfferID, result, tradeRequiresMobileConfirmation, tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive);
	}

	private async Task<ParseTradeResult.EResult> ShouldAcceptTrade(TradeOffer tradeOffer) {
		ArgumentNullException.ThrowIfNull(tradeOffer);

		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
		}

		if (tradeOffer.OtherSteamID64 != 0) {
			// Always deny trades from blacklisted steamIDs
			if (Bot.IsBlacklistedFromTrades(tradeOffer.OtherSteamID64)) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Blacklisted, $"{nameof(tradeOffer.OtherSteamID64)} {tradeOffer.OtherSteamID64}"));

				return ParseTradeResult.EResult.Blacklisted;
			}

			// Always accept trades from SteamMasterID
			if (Bot.GetAccess(tradeOffer.OtherSteamID64) >= EAccess.Master) {
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, $"{nameof(tradeOffer.OtherSteamID64)} {tradeOffer.OtherSteamID64}: {BotConfig.EAccess.Master}"));

				return ParseTradeResult.EResult.Accepted;
			}

			// Deny trades from bad steamIDs if user wishes to do so
			if (ASF.GlobalConfig?.FilterBadBots ?? GlobalConfig.DefaultFilterBadBots) {
				// Keep short timeout allowed for this call, as we don't want to hold the flow for too long
				using CancellationTokenSource archiNetCancellation = new(TimeSpan.FromSeconds(15));

				bool? isBadBot = await ArchiNet.IsBadBot(tradeOffer.OtherSteamID64, archiNetCancellation.Token).ConfigureAwait(false);

				if (isBadBot == true) {
					Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Blacklisted, $"{nameof(tradeOffer.OtherSteamID64)} {tradeOffer.OtherSteamID64}"));

					return ParseTradeResult.EResult.Blacklisted;
				}
			}
		}

		// Check if it's donation trade
		switch (tradeOffer.ItemsToGive.Count) {
			case 0 when tradeOffer.ItemsToReceive.Count == 0:
				// If it's steam issue, try again later
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.TryAgain, $"{nameof(tradeOffer.ItemsToReceive.Count)} = 0"));

				return ParseTradeResult.EResult.TryAgain;
			case 0:
				// Otherwise react accordingly, depending on our preference
				bool acceptDonations = Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.AcceptDonations);
				bool acceptBotTrades = !Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.DontAcceptBotTrades);

				switch (acceptDonations) {
					case true when acceptBotTrades:
						// If we accept donations and bot trades, accept it right away
						Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, $"{nameof(acceptDonations)} = {true} && {nameof(acceptBotTrades)} = {true}"));

						return ParseTradeResult.EResult.Accepted;

					case false when !acceptBotTrades:
						// If we don't accept donations, neither bot trades, deny it right away
						Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, $"{nameof(acceptDonations)} = {false} && {nameof(acceptBotTrades)} = {false}"));

						return ParseTradeResult.EResult.Rejected;
				}

				// Otherwise we either accept donations but not bot trades, or we accept bot trades but not donations
				bool isBotTrade = (tradeOffer.OtherSteamID64 != 0) && Bot.Bots.Values.Any(bot => bot.SteamID == tradeOffer.OtherSteamID64);

				ParseTradeResult.EResult result = (acceptDonations && !isBotTrade) || (acceptBotTrades && isBotTrade) ? ParseTradeResult.EResult.Accepted : ParseTradeResult.EResult.Rejected;

				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, result, $"{nameof(acceptDonations)} = {acceptDonations} && {nameof(acceptBotTrades)} = {acceptBotTrades} && {nameof(isBotTrade)} = {isBotTrade}"));

				return result;
		}

		// If we don't have SteamTradeMatcher enabled, this is the end for us
		if (!Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.SteamTradeMatcher)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, $"{nameof(BotConfig.ETradingPreferences.SteamTradeMatcher)} = {false}"));

			return ParseTradeResult.EResult.Rejected;
		}

		// Decline trade if we're giving more count-wise, this is a very naive pre-check, it'll be strengthened in more detailed fair types exchange next
		if (tradeOffer.ItemsToGive.Count > tradeOffer.ItemsToReceive.Count) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, $"{nameof(tradeOffer.ItemsToGive.Count)}: {tradeOffer.ItemsToGive.Count} > {tradeOffer.ItemsToReceive.Count}"));

			return ParseTradeResult.EResult.Rejected;
		}

		// Decline trade if we're requested to handle any not-accepted item type or if it's not fair games/types exchange
		if (!tradeOffer.IsValidSteamItemsRequest(Bot.BotConfig.MatchableTypes) || !IsFairExchange(tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, $"{nameof(tradeOffer.IsValidSteamItemsRequest)} || {nameof(IsFairExchange)}"));

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
			case > 0 when (holdDuration.Value > (ASF.GlobalConfig?.MaxTradeHoldDuration ?? GlobalConfig.DefaultMaxTradeHoldDuration)) || tradeOffer.ItemsToGive.Any(static item => item.Type is EAssetType.FoilTradingCard or EAssetType.TradingCard && CardsFarmer.SalesBlacklist.Contains(item.RealAppID)):
				Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Rejected, $"{nameof(holdDuration)} > 0: {holdDuration.Value}"));

				return ParseTradeResult.EResult.Rejected;
		}

		// If we're matching everything, this is enough for us
		if (Bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything)) {
			Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, ParseTradeResult.EResult.Accepted, BotConfig.ETradingPreferences.MatchEverything));

			return ParseTradeResult.EResult.Accepted;
		}

		// Get sets we're interested in
		HashSet<(uint RealAppID, EAssetType Type, EAssetRarity Rarity)> wantedSets = tradeOffer.ItemsToGive.Select(static item => (item.RealAppID, item.Type, item.Rarity)).ToHashSet();

		// Now check if it's worth for us to do the trade
		HashSet<Asset> inventory;

		try {
			inventory = await Bot.ArchiHandler.GetMyInventoryAsync().Where(item => !item.IsSteamPointsShopItem && wantedSets.Contains((item.RealAppID, item.Type, item.Rarity))).ToHashSetAsync().ConfigureAwait(false);
		} catch (TimeoutException e) {
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

		bool accept = IsTradeNeutralOrBetter(inventory, tradeOffer.ItemsToGive, tradeOffer.ItemsToReceive);

		// We're now sure whether the trade is neutral+ for us or not
		ParseTradeResult.EResult acceptResult = accept ? ParseTradeResult.EResult.Accepted : ParseTradeResult.EResult.Rejected;

		Bot.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.BotTradeOfferResult, tradeOffer.TradeOfferID, acceptResult, nameof(IsTradeNeutralOrBetter)));

		return acceptResult;
	}
}
