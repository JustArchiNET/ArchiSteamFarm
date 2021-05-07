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
using ArchiSteamFarm.Compatibility;
#endif
using System;
using System.Collections.Generic;
using System.Linq;
using ArchiSteamFarm.Steam.Data;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests {
	[TestClass]
	public sealed class Bot {
		[TestMethod]
		public void MaxItemsBarelyEnoughForOneSet() {
			const uint relevantAppID = 42;

			Dictionary<uint, byte> itemsPerSet = new() {
				{ relevantAppID, Steam.Bot.MinCardsPerBadge },
				{ 43, Steam.Bot.MinCardsPerBadge + 1 }
			};

			HashSet<Asset> items = new();

			foreach ((uint appID, byte cards) in itemsPerSet) {
				for (byte i = 1; i <= cards; i++) {
					items.Add(CreateCard(i, appID));
				}
			}

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, itemsPerSet, Steam.Bot.MinCardsPerBadge);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = items.Where(item => item.RealAppID == relevantAppID)
				.GroupBy(item => (item.RealAppID, item.ContextID, item.ClassID))
				.ToDictionary(grouping => grouping.Key, grouping => (uint) grouping.Sum(item => item.Amount));

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		[ExpectedException(typeof(ArgumentOutOfRangeException))]
		public void MaxItemsTooSmall() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(2, appID)
			};

			GetItemsForFullBadge(items, 2, appID, Steam.Bot.MinCardsPerBadge - 1);

			Assert.Fail();
		}

		[TestMethod]
		public void MoreCardsThanNeeded() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(1, appID),
				CreateCard(2, appID),
				CreateCard(3, appID)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Asset.SteamCommunityContextID, 2), 1 },
				{ (appID, Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void MultipleSets() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(1, appID),
				CreateCard(2, appID),
				CreateCard(2, appID)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 2 },
				{ (appID, Asset.SteamCommunityContextID, 2), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void MultipleSetsDifferentAmount() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, 2),
				CreateCard(2, appID),
				CreateCard(2, appID)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 2 },
				{ (appID, Asset.SteamCommunityContextID, 2), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void MutliRarityAndType() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, type: Asset.EType.TradingCard, rarity: Asset.ERarity.Common),
				CreateCard(2, appID, type: Asset.EType.TradingCard, rarity: Asset.ERarity.Common),

				CreateCard(1, appID, type: Asset.EType.FoilTradingCard, rarity: Asset.ERarity.Uncommon),
				CreateCard(2, appID, type: Asset.EType.FoilTradingCard, rarity: Asset.ERarity.Uncommon),

				CreateCard(1, appID, type: Asset.EType.FoilTradingCard, rarity: Asset.ERarity.Rare),
				CreateCard(2, appID, type: Asset.EType.FoilTradingCard, rarity: Asset.ERarity.Rare),

				// for better readability and easier verification when thinking about this test the items that shall be selected for sending are the ones below this comment
				CreateCard(1, appID, type: Asset.EType.TradingCard, rarity: Asset.ERarity.Uncommon),
				CreateCard(2, appID, type: Asset.EType.TradingCard, rarity: Asset.ERarity.Uncommon),
				CreateCard(3, appID, type: Asset.EType.TradingCard, rarity: Asset.ERarity.Uncommon),

				CreateCard(1, appID, type: Asset.EType.FoilTradingCard, rarity: Asset.ERarity.Common),
				CreateCard(3, appID, type: Asset.EType.FoilTradingCard, rarity: Asset.ERarity.Common),
				CreateCard(7, appID, type: Asset.EType.FoilTradingCard, rarity: Asset.ERarity.Common),

				CreateCard(2, appID, type: Asset.EType.Unknown, rarity: Asset.ERarity.Rare),
				CreateCard(3, appID, type: Asset.EType.Unknown, rarity: Asset.ERarity.Rare),
				CreateCard(4, appID, type: Asset.EType.Unknown, rarity: Asset.ERarity.Rare)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 2 },
				{ (appID, Asset.SteamCommunityContextID, 2), 2 },
				{ (appID, Asset.SteamCommunityContextID, 3), 3 },
				{ (appID, Asset.SteamCommunityContextID, 4), 1 },
				{ (appID, Asset.SteamCommunityContextID, 7), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void NotAllCardsPresent() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(2, appID)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new(0);
			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OneSet() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID),
				CreateCard(2, appID)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Asset.SteamCommunityContextID, 2), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDFullSets() {
			const uint appID0 = 42;
			const uint appID1 = 43;

			HashSet<Asset> items = new() {
				CreateCard(1, appID0),
				CreateCard(1, appID1)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ appID0, 1 },
					{ appID1, 1 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID0, Asset.SteamCommunityContextID, 1), 1 },
				{ (appID1, Asset.SteamCommunityContextID, 1), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherAppIDNoSets() {
			const uint appID0 = 42;
			const uint appID1 = 43;

			HashSet<Asset> items = new() {
				CreateCard(1, appID0),
				CreateCard(1, appID1)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(
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

			HashSet<Asset> items = new() {
				CreateCard(1, appID0),
				CreateCard(2, appID0),

				CreateCard(1, appID1),
				CreateCard(2, appID1),
				CreateCard(3, appID1)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(
				items, new Dictionary<uint, byte> {
					{ appID0, 3 },
					{ appID1, 3 },
					{ appID2, 3 }
				}
			);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID1, Asset.SteamCommunityContextID, 1), 1 },
				{ (appID1, Asset.SteamCommunityContextID, 2), 1 },
				{ (appID1, Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityFullSets() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, rarity: Asset.ERarity.Common),
				CreateCard(1, appID, rarity: Asset.ERarity.Rare)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 1, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityNoSets() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, rarity: Asset.ERarity.Common),
				CreateCard(1, appID, rarity: Asset.ERarity.Rare)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherRarityOneSet() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, rarity: Asset.ERarity.Common),
				CreateCard(2, appID, rarity: Asset.ERarity.Common),
				CreateCard(1, appID, rarity: Asset.ERarity.Uncommon),
				CreateCard(2, appID, rarity: Asset.ERarity.Uncommon),
				CreateCard(3, appID, rarity: Asset.ERarity.Uncommon)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Asset.SteamCommunityContextID, 2), 1 },
				{ (appID, Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeFullSets() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, type: Asset.EType.TradingCard),
				CreateCard(1, appID, type: Asset.EType.FoilTradingCard)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 1, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 2 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeNoSets() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, type: Asset.EType.TradingCard),
				CreateCard(1, appID, type: Asset.EType.FoilTradingCard)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new(0);

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void OtherTypeOneSet() {
			const uint appID = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID, type: Asset.EType.TradingCard),
				CreateCard(2, appID, type: Asset.EType.TradingCard),
				CreateCard(1, appID, type: Asset.EType.FoilTradingCard),
				CreateCard(2, appID, type: Asset.EType.FoilTradingCard),
				CreateCard(3, appID, type: Asset.EType.FoilTradingCard)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 3, appID);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID, Asset.SteamCommunityContextID, 1), 1 },
				{ (appID, Asset.SteamCommunityContextID, 2), 1 },
				{ (appID, Asset.SteamCommunityContextID, 3), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void TooHighAmount() {
			const uint appID0 = 42;

			HashSet<Asset> items = new() {
				CreateCard(1, appID0, 2),
				CreateCard(2, appID0)
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID0);

			Dictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult = new() {
				{ (appID0, Asset.SteamCommunityContextID, 1), 1 },
				{ (appID0, Asset.SteamCommunityContextID, 2), 1 }
			};

			AssertResultMatchesExpectation(expectedResult, itemsToSend);
		}

		[TestMethod]
		public void TooManyCardsForSingleTrade() {
			const uint appID = 42;

			HashSet<Asset> items = new();

			for (byte i = 0; i < Steam.Exchange.Trading.MaxItemsPerTrade; i++) {
				items.Add(CreateCard(1, appID));
				items.Add(CreateCard(2, appID));
			}

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, 2, appID);

			Assert.IsTrue(itemsToSend.Count <= Steam.Exchange.Trading.MaxItemsPerTrade);
		}

		[TestMethod]
		public void TooManyCardsForSingleTradeMultipleAppIDs() {
			const uint appID0 = 42;
			const uint appID1 = 43;

			HashSet<Asset> items = new();

			for (byte i = 0; i < 100; i++) {
				items.Add(CreateCard(1, appID0));
				items.Add(CreateCard(2, appID0));
				items.Add(CreateCard(1, appID1));
				items.Add(CreateCard(2, appID1));
			}

			Dictionary<uint, byte> itemsPerSet = new() {
				{ appID0, 2 },
				{ appID1, 2 }
			};

			HashSet<Asset> itemsToSend = GetItemsForFullBadge(items, itemsPerSet);

			Assert.IsTrue(itemsToSend.Count <= Steam.Exchange.Trading.MaxItemsPerTrade);
		}

		[TestMethod]
		[ExpectedException(typeof(InvalidOperationException))]
		public void TooManyCardsPerSet() {
			const uint appID0 = 42;
			const uint appID1 = 43;
			const uint appID2 = 44;

			HashSet<Asset> items = new() {
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

		private static void AssertResultMatchesExpectation(IReadOnlyDictionary<(uint RealAppID, ulong ContextID, ulong ClassID), uint> expectedResult, IReadOnlyCollection<Asset> itemsToSend) {
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

		private static Asset CreateCard(ulong classID, uint realAppID, uint amount = 1, Asset.EType type = Asset.EType.TradingCard, Asset.ERarity rarity = Asset.ERarity.Common) => new(Asset.SteamAppID, Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: rarity);

		private static HashSet<Asset> GetItemsForFullBadge(IReadOnlyCollection<Asset> inventory, byte cardsPerSet, uint appID, ushort maxItems = Steam.Exchange.Trading.MaxItemsPerTrade) => GetItemsForFullBadge(inventory, new Dictionary<uint, byte> { { appID, cardsPerSet } }, maxItems);

		private static HashSet<Asset> GetItemsForFullBadge(IReadOnlyCollection<Asset> inventory, IDictionary<uint, byte> cardsPerSet, ushort maxItems = Steam.Exchange.Trading.MaxItemsPerTrade) {
			Dictionary<(uint RealAppID, Asset.EType Type, Asset.ERarity Rarity), List<uint>> inventorySets = Steam.Exchange.Trading.GetInventorySets(inventory);

			return Steam.Bot.GetItemsForFullSets(inventory, inventorySets.ToDictionary(kv => kv.Key, kv => (SetsToExtract: inventorySets[kv.Key][0], cardsPerSet[kv.Key.RealAppID])), maxItems).ToHashSet();
		}
	}
}
