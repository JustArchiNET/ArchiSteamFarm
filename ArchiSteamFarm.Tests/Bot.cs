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

using System;
using System.Collections.Generic;
using System.Linq;
using ArchiSteamFarm.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests {
	[TestClass]
	public sealed class Bot {
		[TestMethod]
		public void MoreCardsThanNeeded() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(1, appID),
				CreateCard(2, appID),
				CreateCard(3, appID)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void TooManyCardsForSingleTrade() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new();

			for (byte i = 0; i < ArchiSteamFarm.Trading.MaxItemsPerTrade; i++) {
				items.Add(CreateCard(1, appID));
				items.Add(CreateCard(2, appID));
			}

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Assert.IsTrue(itemsToSend.Count <= ArchiSteamFarm.Trading.MaxItemsPerTrade);
		}

		[TestMethod]
		public void MultipleSets() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(1, appID),
				CreateCard(2, appID),
				CreateCard(2, appID)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 2 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 2), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void MultipleSetsDifferentAmount() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, 2),
				CreateCard(2, appID),
				CreateCard(2, appID)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 2 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 2), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void MutliRarityAndType() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Common),
				CreateCard(2, appID, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Common),

				CreateCard(1, appID, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(2, appID, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Uncommon),

				CreateCard(1, appID, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(2, appID, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Rare),

				// for better readability and easier verification when thinking about this test the items that shall be selected for sending are the ones below this comment
				CreateCard(1, appID, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(2, appID, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(3, appID, type: Steam.Asset.EType.TradingCard, rarity: Steam.Asset.ERarity.Uncommon),

				CreateCard(1, appID, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Common),
				CreateCard(3, appID, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Common),
				CreateCard(7, appID, type: Steam.Asset.EType.FoilTradingCard, rarity: Steam.Asset.ERarity.Common),

				CreateCard(2, appID, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(3, appID, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Rare),
				CreateCard(4, appID, type: Steam.Asset.EType.Unknown, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 2 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 2), 2 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 3), 3 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 4), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 7), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void NotAllCardsPresent() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(2, appID)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new(0);
			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OneSet() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(2, appID)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 2), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDFullSets() {
			const uint appID0 = 42;
			const uint appID1 = 43;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID0),
				CreateCard(1, appID1)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ appID0, 1 },
					{ appID1, 1 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID0, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (appID1, Steam.Asset.SteamCommunityContextID, 1), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDNoSets() {
			const uint appID0 = 42;
			const uint appID1 = 43;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID0),
				CreateCard(1, appID1)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ appID0, 2 },
					{ appID1, 2 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDOneSet() {
			const uint appID0 = 42;
			const uint appID1 = 43;
			const uint appID2 = 44;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID0),
				CreateCard(2, appID0),

				CreateCard(1, appID1),
				CreateCard(2, appID1),
				CreateCard(3, appID1)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ appID0, 3 },
					{ appID1, 3 },
					{ appID2, 3 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID1, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (appID1, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (appID1, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityFullSets() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, rarity: Steam.Asset.ERarity.Common),
				CreateCard(1, appID, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 1, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityNoSets() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, rarity: Steam.Asset.ERarity.Common),
				CreateCard(1, appID, rarity: Steam.Asset.ERarity.Rare)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityOneSet() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, rarity: Steam.Asset.ERarity.Common),
				CreateCard(2, appID, rarity: Steam.Asset.ERarity.Common),
				CreateCard(1, appID, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(2, appID, rarity: Steam.Asset.ERarity.Uncommon),
				CreateCard(3, appID, rarity: Steam.Asset.ERarity.Uncommon)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeFullSets() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, appID, type: Steam.Asset.EType.FoilTradingCard)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 1, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeNoSets() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, appID, type: Steam.Asset.EType.FoilTradingCard)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeOneSet() {
			const uint appID = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID, type: Steam.Asset.EType.TradingCard),
				CreateCard(2, appID, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, appID, type: Steam.Asset.EType.FoilTradingCard),
				CreateCard(2, appID, type: Steam.Asset.EType.FoilTradingCard),
				CreateCard(3, appID, type: Steam.Asset.EType.FoilTradingCard)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 2), 1 },
				{ (appID, Steam.Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void TooHighAmount() {
			const uint appID0 = 42;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID0, 2),
				CreateCard(2, appID0)
			};

			HashSet<Steam.Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID0);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID0, Steam.Asset.SteamCommunityContextID, 1), 1 },
				{ (appID0, Steam.Asset.SteamCommunityContextID, 2), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		[ExpectedException(typeof(InvalidOperationException))]
		public void TooManyCardsPerSet() {
			const uint appID0 = 42;
			const uint appID1 = 43;
			const uint appID2 = 44;

			HashSet<Steam.Asset> items = new() {
				CreateCard(1, appID0),
				CreateCard(2, appID0),
				CreateCard(3, appID0),
				CreateCard(4, appID0)
			};

			GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ appID0, 3 },
					{ appID1, 3 },
					{ appID2, 3 }
				}
			);

			Assert.Fail();
		}

		private static void AssertResultMatchesExpectation(IReadOnlyDictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult, IReadOnlyCollection<Steam.Asset> itemsToSend) {
			if (expectedResult == null) {
				throw new ArgumentNullException(nameof(expectedResult));
			}

			if (itemsToSend == null) {
				throw new ArgumentNullException(nameof(itemsToSend));
			}

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), long> realResult = itemsToSend.GroupBy(asset => (asset.RealAppID, asset.ContextID, asset.ClassID)).ToDictionary(group => group.Key, group => group.Sum(asset => asset.Amount));
			Assert.AreEqual(expectedResult.Count, realResult.Count);
			Assert.IsTrue(expectedResult.All(expectation => realResult.TryGetValue(expectation.Key, out long reality) && (expectation.Value == reality)));
		}

		private static Steam.Asset CreateCard(ulong classID, uint realAppID, uint amount = 1, Steam.Asset.EType type = Steam.Asset.EType.TradingCard, Steam.Asset.ERarity rarity = Steam.Asset.ERarity.Common) => new(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: rarity);

		private static HashSet<Steam.Asset> GetItemsForFullBadge(IReadOnlyCollection<Steam.Asset> inventory, byte cardsPerSet, uint appID) => GetItemsForFullBadge(inventory, new Dictionary<uint, byte> { { appID, cardsPerSet } });

		private static HashSet<Steam.Asset> GetItemsForFullBadge(IReadOnlyCollection<Steam.Asset> inventory, IDictionary<uint, byte> cardsPerSet) {
			Dictionary<(uint RealAppID, Steam.Asset.EType Type, Steam.Asset.ERarity Rarity), List<uint>> inventorySets = ArchiSteamFarm.Trading.GetInventorySets(inventory);

			return ArchiSteamFarm.Bot.GetItemsForFullSets(inventory, inventorySets.ToDictionary(kv => kv.Key, kv => (SetsToExtract: inventorySets[kv.Key][0], cardsPerSet[kv.Key.RealAppID]))).ToHashSet();
		}
	}
}
