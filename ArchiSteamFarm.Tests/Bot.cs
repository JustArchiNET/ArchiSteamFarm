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
using static ArchiSteamFarm.Bot;

namespace ArchiSteamFarm.Tests {
	[TestClass]
	public sealed class Bot {
		[TestMethod]
		public void NotAllCardsPresent() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 3).ToHashSet();
			AssertFullSets(itemsToSend, 3, 0);
		}

		[TestMethod]
		public void OneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2).ToHashSet();
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

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2).ToHashSet();
			AssertFullSets(itemsToSend, 2, 2);
		}

		[TestMethod]
		public void MultipleSetsDifferentAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2).ToHashSet();
			AssertFullSets(itemsToSend, 2, 2);
		}

		[TestMethod]
		public void MoreCardsThanNeeded() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(1),
				CreateCard(2),
				CreateCard(3),
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 3).ToHashSet();
			AssertFullSets(itemsToSend, 3, 1);
		}

		[TestMethod]
		public void TooHighAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2).ToHashSet();
			AssertFullSets(itemsToSend, 2, 1);
		}

		[TestMethod]
		public void PartiallyHighAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 5),
				CreateCard(1, 4),
				CreateCard(1),
				CreateCard(2, 2),
				CreateCard(2, 2),
				CreateCard(2, 2),
				CreateCard(2, 2),
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2).ToHashSet();
			AssertFullSets(itemsToSend, 2, 8);
		}

		[TestMethod]
		[ExpectedException(typeof(ArgumentException))]
		public void SeveralRealAppIDs() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, realAppID: 42),
				CreateCard(1, realAppID: 43)
			};

			// ReSharper disable once ReturnValueOfPureMethodIsNotUsed
			GetItemsForFullBadge(items, 2).ToHashSet();
			Assert.Fail();
		}

		[TestMethod]
		[ExpectedException(typeof(ArgumentException))]
		public void SeveralAssetTypes() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard),
				CreateCard(1, type: Steam.Asset.EType.Emoticon)
			};

			// ReSharper disable once ReturnValueOfPureMethodIsNotUsed
			GetItemsForFullBadge(items, 42).ToHashSet();
			Assert.Fail();
		}

		private static Steam.Asset CreateCard(ulong classID, uint amount = 1, uint realAppID = 42, Steam.Asset.EType type = Steam.Asset.EType.TradingCard) => new Steam.Asset(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: Steam.Asset.ERarity.Common);

		private static void AssertFullSets(HashSet<Steam.Asset> itemsToSend, byte cardsInSet, byte expectedSets) {
			if (expectedSets > 0) {
				AssertAllCardsPresent(itemsToSend, cardsInSet);
				AssertEqualAmounts(itemsToSend);
				AssertEqualRealAppID(itemsToSend);
				AssertEqualType(itemsToSend);
			}

			Assert.AreEqual(expectedSets, itemsToSend.GroupBy(item => item.ClassID).FirstOrDefault()?.Select(item => item.Amount).Aggregate((a, b) => a + b) ?? 0);
		}

		private static void AssertEqualAmounts(HashSet<Steam.Asset> itemsToSend) => Assert.AreEqual(1, itemsToSend.GroupBy(item => item.ClassID).Select(group => group.Select(item => item.Amount).Aggregate((a, b) => a + b)).GroupBy(count => count).Count());

		private static void AssertEqualRealAppID(HashSet<Steam.Asset> itemsToSend) => Assert.AreEqual(1, itemsToSend.GroupBy(item => item.RealAppID).Count());

		private static void AssertEqualType(HashSet<Steam.Asset> itemsToSend) => Assert.AreEqual(1, itemsToSend.GroupBy(anyItem => anyItem.Type).Count());

		private static void AssertAllCardsPresent(HashSet<Steam.Asset> itemsToSend, byte cardsInSet) => Assert.AreEqual(cardsInSet, itemsToSend.GroupBy(item => item.ClassID).Count());
	}
}
