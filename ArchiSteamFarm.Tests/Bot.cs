//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Linq;
using ArchiSteamFarm.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests {
	[TestClass]
	public sealed class Bot {
		private const uint DefaultRealAppID = 42;
		private const Steam.Asset.EType DefaultAssetType = Steam.Asset.EType.TradingCard;
		private const Steam.Asset.ERarity DefaultRarity = Steam.Asset.ERarity.Common;
		private const uint DefaultAmountPerCard = 0;
		private const byte DefaultCardsPerSet = byte.MaxValue;

		[TestMethod]
		public void NotAllCardsPresent() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3, amountPerCard: 1);
			AssertFullSets(itemsToSend, 3, 0);
		}

		[TestMethod]
		public void OneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2, amountPerCard: 1);
			AssertFullSets(itemsToSend, 2, 1);
		}

		[TestMethod]
		public void MultipleSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(1),
				CreateCard(2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2, amountPerCard: 2);
			AssertFullSets(itemsToSend, 2, 2);
		}

		[TestMethod]
		public void MultipleSetsDifferentAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2, amountPerCard: 2);
			AssertFullSets(itemsToSend, 2, 2);
		}

		[TestMethod]
		public void MoreCardsThanNeeded() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(1),
				CreateCard(2),
				CreateCard(3)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3, amountPerCard: 1);
			AssertFullSets(itemsToSend, 3, 1);
		}

		[TestMethod]
		public void CardsWithOtherRarity() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, rarity: DefaultRarity),
				CreateCard(1, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1, amountPerCard: 1);
			AssertFullSets(itemsToSend, 1, 1);
		}

		[TestMethod]
		public void CardsWithOtherType() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: DefaultAssetType),
				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1, amountPerCard: 1);
			AssertFullSets(itemsToSend, 1, 1);
		}

		[TestMethod]
		public void CardsWithOtherAppID() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, DefaultRealAppID),
				CreateCard(1, DefaultRealAppID + 1),
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1, amountPerCard: 1);
			AssertFullSets(itemsToSend, 1, 1);
		}

		[TestMethod]
		public void TooHighAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2, amountPerCard: 1);
			AssertFullSets(itemsToSend, 2, 1);
		}

		private static HashSet<Steam.Asset> GetItemsForFullBadge(IReadOnlyCollection<Steam.Asset> inventory, uint appID = DefaultRealAppID, Steam.Asset.EType type = DefaultAssetType, Steam.Asset.ERarity rarity = DefaultRarity, byte cardsPerSet = DefaultCardsPerSet, uint amountPerCard = DefaultAmountPerCard) =>
			ArchiSteamFarm.Bot.GetItemsForFullBadge(
				inventory, new Dictionary<(uint RealAppID, Steam.Asset.EType Type, Steam.Asset.ERarity Rarity), (uint SetsToExtract, byte CardsPerSet)> {
					{ (appID, type, rarity), (amountPerCard, cardsPerSet) }
				}
			).ToHashSet();

		private static Steam.Asset CreateCard(ulong classID, uint amount = 1, uint realAppID = DefaultRealAppID, Steam.Asset.EType type = DefaultAssetType, Steam.Asset.ERarity rarity = DefaultRarity) => new Steam.Asset(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: rarity);

		private static void AssertFullSets(HashSet<Steam.Asset> itemsToSend, byte cardsInSet, byte expectedSets) {
			if (expectedSets > 0) {
				AssertAllCardsPresent(itemsToSend, cardsInSet);
				AssertEqualAmounts(itemsToSend);
				AssertEqualRealAppID(itemsToSend);
				AssertEqualType(itemsToSend);
			}

			Assert.AreEqual(expectedSets, itemsToSend.GroupBy(item => item.ClassID).FirstOrDefault()?.Select(item => item.Amount).Aggregate((a, b) => a + b) ?? 0);
		}

		private static void AssertEqualAmounts(IEnumerable<Steam.Asset> itemsToSend) => Assert.AreEqual(1, itemsToSend.GroupBy(item => item.ClassID).Select(group => group.Select(item => item.Amount).Aggregate((a, b) => a + b)).GroupBy(count => count).Count());

		private static void AssertEqualRealAppID(IEnumerable<Steam.Asset> itemsToSend) => Assert.AreEqual(1, itemsToSend.GroupBy(item => item.RealAppID).Count());

		private static void AssertEqualType(IEnumerable<Steam.Asset> itemsToSend) => Assert.AreEqual(1, itemsToSend.GroupBy(anyItem => anyItem.Type).Count());

		private static void AssertAllCardsPresent(IEnumerable<Steam.Asset> itemsToSend, byte cardsInSet) => Assert.AreEqual(cardsInSet, itemsToSend.GroupBy(item => item.ClassID).Count());
	}
}
