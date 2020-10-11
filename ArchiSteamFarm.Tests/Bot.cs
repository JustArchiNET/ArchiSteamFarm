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

using System.Collections.Generic;
using System.Linq;
using ArchiSteamFarm.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests {
	[TestClass]
	public sealed class Bot {
		[TestMethod]
		public void NotAllCardsPresent() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3, amountPerCard: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint>(0);
			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2, amountPerCard: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
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

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 2 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void MultipleSetsDifferentAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2, amountPerCard: 2);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 2 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
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

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void CardsWithOtherRarity() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, rarity: Steam.Asset.ERarity.Common),
				CreateCard(1, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1, amountPerCard: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void CardsWithOtherType() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1, amountPerCard: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void CardsWithOtherAppID() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 42),
				CreateCard(1, (uint) 42 + 1),
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1, amountPerCard: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void TooHighAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2, amountPerCard: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		private static HashSet<Steam.Asset> GetItemsForFullBadge(IReadOnlyCollection<Steam.Asset> inventory, uint appID = (uint) 42, Steam.Asset.EType type = Steam.Asset.EType.TradingCard, Steam.Asset.ERarity rarity = Steam.Asset.ERarity.Common, byte cardsPerSet = byte.MaxValue, uint amountPerCard = (uint) 0) =>
			ArchiSteamFarm.Bot.GetItemsForFullBadge(
				inventory, new Dictionary<(uint RealAppID, Steam.Asset.EType Type, Steam.Asset.ERarity Rarity), (uint SetsToExtract, byte CardsPerSet)> {
					{ (appID, type, rarity), (amountPerCard, cardsPerSet) }
				}
			).ToHashSet();

		private static Steam.Asset CreateCard(ulong classID, uint amount = 1, uint realAppID = (uint) 42, Steam.Asset.EType type = Steam.Asset.EType.TradingCard, Steam.Asset.ERarity rarity = Steam.Asset.ERarity.Common) => new Steam.Asset(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: rarity);

		private static void AssertResultMatchesExpectation(Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult, IEnumerable<Steam.Asset> itemsToSend) {
			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), long> realResult = itemsToSend.GroupBy(asset => (asset.RealAppID, asset.ContextID, asset.ClassID)).ToDictionary(group => group.Key, group => group.Sum(asset => asset.Amount));
			Assert.AreEqual(expectedResult.Count, realResult.Count);
			Assert.IsTrue(expectedResult.All(expectation => realResult.TryGetValue(expectation.Key, out long reality) && (expectation.Value == reality)));
		}
	}
}
