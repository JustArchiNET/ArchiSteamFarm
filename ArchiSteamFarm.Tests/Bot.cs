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

			HashSet<Steam.Asset>? itemsToSend = GetItemsForFullBadge(items, 3);
			Assert.IsNotNull(itemsToSend);
			Assert.IsTrue(itemsToSend.Count == 0);
		}

		[TestMethod]
		public void OneSet() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(2)
			};

			HashSet<Steam.Asset>? itemsToSend = GetItemsForFullBadge(items, 2);
			Assert.IsNotNull(itemsToSend);
			Assert.IsTrue(itemsToSend.Count == items.Count);
		}

		[TestMethod]
		public void MultipleSets() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(1),
				CreateCard(2),
				CreateCard(2)
			};

			HashSet<Steam.Asset>? itemsToSend = GetItemsForFullBadge(items, 2);
			Assert.IsNotNull(itemsToSend);
			Assert.IsTrue(itemsToSend.Count == items.Count);
		}

		[TestMethod]
		public void MultipleSetsDifferentAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2),
				CreateCard(2)
			};

			HashSet<Steam.Asset>? itemsToSend = GetItemsForFullBadge(items, 2);
			Assert.IsNotNull(itemsToSend);
			Assert.IsTrue(itemsToSend.Count == items.Count);
		}

		[TestMethod]
		public void MoreCardsThanNeeded() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1),
				CreateCard(1),
				CreateCard(2),
				CreateCard(3),
			};

			HashSet<Steam.Asset>? itemsToSend = GetItemsForFullBadge(items, 3);
			Assert.IsNotNull(itemsToSend);
			Assert.IsTrue(itemsToSend.Count == 3);
		}

		[TestMethod]
		public void TooHighAmount() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, 2),
				CreateCard(2)
			};

			HashSet<Steam.Asset>? itemsToSend = GetItemsForFullBadge(items, 2);
			Assert.IsNotNull(itemsToSend);
			Assert.IsTrue(itemsToSend.Count == 0);
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

			HashSet<Steam.Asset>? itemsToSend = GetItemsForFullBadge(items, 2);
			Assert.IsNotNull(itemsToSend);
			Assert.IsTrue(itemsToSend.Count == 5);
		}

		[TestMethod]
		public void SeveralRealAppIDs() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, realAppID: 42),
				CreateCard(1, realAppID: 43)
			};

			try {
				GetItemsForFullBadge(items, 2);
				Assert.Fail();
			} catch (ArgumentException) { }
		}

		[TestMethod]
		public void SeveralAssetTypes() {
			HashSet<Steam.Asset> items = new HashSet<Steam.Asset> {
				CreateCard(1, type: Steam.Asset.EType.TradingCard),
				CreateCard(1, type: Steam.Asset.EType.FoilTradingCard),
				CreateCard(1, type: Steam.Asset.EType.Emoticon)
			};

			try {
				GetItemsForFullBadge(items, 42);
				Assert.Fail();
			} catch (ArgumentException) { }
		}

		private static Steam.Asset CreateCard(ulong classID, uint amount = 1, uint realAppID = 42, Steam.Asset.EType type = Steam.Asset.EType.TradingCard) => new Steam.Asset(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: Steam.Asset.ERarity.Common);
	}
}
