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

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint>(0);
			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2);

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

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2);

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

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2);

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

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityFullSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, rarity: Steam.Asset.ERarity.Common),
				CreateCard(1, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityNoSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, rarity: Steam.Asset.ERarity.Common),
				CreateCard(1, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint>(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityOneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, rarity: Steam.Asset.ERarity.Common),
				CreateCard(2, rarity: Steam.Asset.ERarity.Common),
				CreateCard(1, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(2, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(3, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(1, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(2, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(3, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(4, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeFullSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 1);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeNoSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint>(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeOneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: Steam.Asset.EType.TradingCard),
				CreateCard(2, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard),
				CreateCard(2, type: Steam.Asset.EType.FoilTradingCard),
				CreateCard(3, type: Steam.Asset.EType.FoilTradingCard),
				CreateCard(1, type: Steam.Asset.EType.Unknown),
				CreateCard(2, type: Steam.Asset.EType.Unknown),
				CreateCard(3, type: Steam.Asset.EType.Unknown),
				CreateCard(4, type: Steam.Asset.EType.Unknown)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void MutliRarityAndType() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Common),
				CreateCard(2, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Common),

				CreateCard(1, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Common),
				CreateCard(2, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Common),
				CreateCard(3, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Common),
				CreateCard(4, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Common),

				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(2, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Uncommon),

				CreateCard(1, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(2, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(3, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(4, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Uncommon),

				CreateCard(1, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(2, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(3, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(4, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Rare),

				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(2, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Rare),

				// for better readability and easier verification when thinking about this test the items that shall be selected for sending are the ones below this comment
				CreateCard(1, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(2, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(3, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Uncommon),

				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Common),
				CreateCard(3, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Common),
				CreateCard(7, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Common),

				CreateCard(2, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(3, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(4, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 3);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 2 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 2 },
				{ (42, Steam.Asset.SteamCommunityContextID, 3), 3 },
				{ (42, Steam.Asset.SteamCommunityContextID, 4), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 7), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDFullSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, realAppID: 42),
				CreateCard(1, realAppID: 43),
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ 42, 1 },
					{ 43, 1 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (43, Steam.Asset.SteamCommunityContextID, 1), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDNoSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, realAppID: 42),
				CreateCard(1, realAppID: 43),
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ 42, 2 },
					{ 43, 2 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint>(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDOneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, realAppID: 42),
				CreateCard(2, realAppID: 42),
				CreateCard(1, realAppID: 43),
				CreateCard(2, realAppID: 43),
				CreateCard(3, realAppID: 43),
				CreateCard(1, realAppID: 44),
				CreateCard(2, realAppID: 44),
				CreateCard(3, realAppID: 44),
				CreateCard(4, realAppID: 44)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ 42, 3 },
					{ 43, 3 },
					{ 44, 3 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (43, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (43, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (43, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void TooHighAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, cardsPerSet: 2);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> {
				{ (42, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (42, Steam.Asset.SteamCommunityContextID, 2), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		private static HashSet<Steam.Asset> GetItemsForFullBadge(IReadOnlyCollection<Steam.Asset> inventory, uint appID = (uint) 42, byte cardsPerSet = byte.MaxValue) => GetItemsForFullBadge(inventory, new Dictionary<uint, byte> { { appID, cardsPerSet } });

		private static HashSet<Steam.Asset> GetItemsForFullBadge(IReadOnlyCollection<Steam.Asset> inventory, IDictionary<uint, byte> cardsPerSet) {
			Dictionary<(uint RealAppID, Steam.Asset.EType Type, Steam.Asset.ERarity Rarity), List<uint>> inventorySets = ArchiSteamFarm.Trading.GetInventorySets(inventory);

			return ArchiSteamFarm.Bot.GetItemsForFullBadge(inventory, inventorySets.ToDictionary(kv => kv.Key, kv => (SetsToExtract: inventorySets[kv.Key][0], cardsPerSet[kv.Key.RealAppID]))).ToHashSet();
		}

		private static Steam.Asset CreateCard(ulong classID, uint amount = 1, uint realAppID = (uint) 42, Steam.Asset.EType type = Steam.Asset.EType.TradingCard, Steam.Asset.ERarity rarity = Steam.Asset.ERarity.Common) => new Steam.Asset(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: rarity);

		private static void AssertResultMatchesExpectation(Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult, IEnumerable<Steam.Asset> itemsToSend) {
			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), long> realResult = itemsToSend.GroupBy(asset => (asset.RealAppID, asset.ContextID, asset.ClassID)).ToDictionary(group => group.Key, group => group.Sum(asset => asset.Amount));
			Assert.AreEqual(expectedResult.Count, realResult.Count);
			Assert.IsTrue(expectedResult.All(expectation => realResult.TryGetValue(expectation.Key, out long reality) && (expectation.Value == reality)));
		}
	}
}
