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

using System.Collections.Generic;
using ArchiSteamFarm.Steam.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static ArchiSteamFarm.Steam.Exchange.Trading;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[TestClass]
internal sealed class Trading {
	[TestMethod]
	internal void ExploitingNewSetsIsFairButNotNeutral() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 40),
			CreateItem(2, amount: 10),
			CreateItem(3, amount: 10)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(2, amount: 5),
			CreateItem(3, amount: 5)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(1, amount: 9),
			CreateItem(4)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void Issue3203() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 2),
			CreateItem(2, amount: 6),
			CreateItem(3),
			CreateItem(4)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(2, amount: 2)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(5),
			CreateItem(6),
			CreateItem(7)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void MismatchRarityIsNotFair() {
		HashSet<Asset> itemsToGive = [
			CreateItem(1, rarity: EAssetRarity.Rare)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2)
		];

		Assert.IsFalse(IsFairExchange(itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void MismatchRealAppIDsIsNotFair() {
		HashSet<Asset> itemsToGive = [
			CreateItem(1, realAppID: 570)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2)
		];

		Assert.IsFalse(IsFairExchange(itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void MismatchTypesIsNotFair() {
		HashSet<Asset> itemsToGive = [
			CreateItem(1, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2)
		];

		Assert.IsFalse(IsFairExchange(itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void MultiGameMultiTypeBadReject() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 9),
			CreateItem(3, amount: 9, realAppID: 730, type: EAssetType.Emoticon),
			CreateItem(4, realAppID: 730, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(4, realAppID: 730, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2),
			CreateItem(3, realAppID: 730, type: EAssetType.Emoticon)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void MultiGameMultiTypeNeutralAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 9),
			CreateItem(3, realAppID: 730, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(3, realAppID: 730, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2),
			CreateItem(4, realAppID: 730, type: EAssetType.Emoticon)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void MultiGameSingleTypeBadReject() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 9),
			CreateItem(3, realAppID: 730),
			CreateItem(4, realAppID: 730)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(3, realAppID: 730)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2),
			CreateItem(4, realAppID: 730)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void MultiGameSingleTypeNeutralAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 2),
			CreateItem(3, realAppID: 730)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(3, realAppID: 730)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2),
			CreateItem(4, realAppID: 730)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameAbrynosWasWrongNeutralAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1),
			CreateItem(2, amount: 2),
			CreateItem(3),
			CreateItem(4),
			CreateItem(5)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(2)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(3)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameDonationAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2),
			CreateItem(3, type: EAssetType.SteamGems)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameMultiTypeBadReject() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 9),
			CreateItem(3, amount: 9, type: EAssetType.Emoticon),
			CreateItem(4, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(4, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2),
			CreateItem(3, type: EAssetType.Emoticon)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameMultiTypeNeutralAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 9),
			CreateItem(3, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(3, type: EAssetType.Emoticon)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2),
			CreateItem(4, type: EAssetType.Emoticon)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameQuantityBadReject() {
		HashSet<Asset> inventory = [
			CreateItem(1),
			CreateItem(2),
			CreateItem(3)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(2),
			CreateItem(3)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(4, amount: 3)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameQuantityBadReject2() {
		HashSet<Asset> inventory = [
			CreateItem(1),
			CreateItem(2, amount: 2)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(2, amount: 2)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(3, amount: 3)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameQuantityNeutralAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 2),
			CreateItem(2)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1),
			CreateItem(2)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(3, amount: 2)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameSingleTypeBadReject() {
		HashSet<Asset> inventory = [
			CreateItem(1),
			CreateItem(2)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameSingleTypeBadWithOverpayingReject() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 2),
			CreateItem(2, amount: 2),
			CreateItem(3, amount: 2)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(2)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(1),
			CreateItem(3)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameSingleTypeBigDifferenceAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1),
			CreateItem(2, amount: 5),
			CreateItem(3)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(2)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(3)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameSingleTypeBigDifferenceReject() {
		HashSet<Asset> inventory = [
			CreateItem(1),
			CreateItem(2, amount: 2),
			CreateItem(3, amount: 2),
			CreateItem(4, amount: 3),
			CreateItem(5, amount: 10)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(2),
			CreateItem(5)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(3),
			CreateItem(4)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameSingleTypeGoodAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 2)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameSingleTypeNeutralAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(1)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(2)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void SingleGameSingleTypeNeutralWithOverpayingAccept() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 2),
			CreateItem(2, amount: 2)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(2)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(1),
			CreateItem(3)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	internal void TakingExcessiveAmountOfSingleCardCanStillBeFairAndNeutral() {
		HashSet<Asset> inventory = [
			CreateItem(1, amount: 52),
			CreateItem(2, amount: 73),
			CreateItem(3, amount: 52),
			CreateItem(4, amount: 47),
			CreateItem(5)
		];

		HashSet<Asset> itemsToGive = [
			CreateItem(2, amount: 73)
		];

		HashSet<Asset> itemsToReceive = [
			CreateItem(1, amount: 9),
			CreateItem(3, amount: 9),
			CreateItem(4, amount: 8),
			CreateItem(5, amount: 24),
			CreateItem(6, amount: 23)
		];

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	private static Asset CreateItem(ulong classID, ulong instanceID = 0, uint amount = 1, bool marketable = false, bool tradable = false, uint realAppID = Asset.SteamAppID, EAssetType type = EAssetType.TradingCard, EAssetRarity rarity = EAssetRarity.Common) => new(Asset.SteamAppID, Asset.SteamCommunityContextID, classID, amount, new InventoryDescription(Asset.SteamAppID, classID, instanceID, marketable, tradable, realAppID, type, rarity));
}
#pragma warning restore CA1812 // False positive, the class is used during MSTest
