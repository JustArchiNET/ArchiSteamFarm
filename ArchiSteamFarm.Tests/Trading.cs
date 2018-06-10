//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Collections.Generic;
using System.Reflection;
using ArchiSteamFarm.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests {
	[TestClass]
	public sealed class Trading {
		[TestMethod]
		public void MultiGameMultiTypeBadReject() {
			Steam.Asset item1Type1Game1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1Type1Game1X9 = GenerateSteamCommunityItem(1, 9, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Type1Game1 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			Steam.Asset item3Type2Game2 = GenerateSteamCommunityItem(3, 1, 730, Steam.Asset.EType.Emoticon);
			Steam.Asset item3Type2Game2X9 = GenerateSteamCommunityItem(3, 9, 730, Steam.Asset.EType.Emoticon);
			Steam.Asset item4Type2Game2 = GenerateSteamCommunityItem(4, 1, 730, Steam.Asset.EType.Emoticon);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1Type1Game1X9, item3Type2Game2X9, item4Type2Game2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1Type1Game1, item4Type2Game2 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2Type1Game1, item3Type2Game2 };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void MultiGameMultiTypeNeutralAccept() {
			Steam.Asset item1Type1Game1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1Type1Game1X9 = GenerateSteamCommunityItem(1, 9, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Type1Game1 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			Steam.Asset item3Type2Game2 = GenerateSteamCommunityItem(3, 1, 730, Steam.Asset.EType.Emoticon);
			Steam.Asset item4Type2Game2 = GenerateSteamCommunityItem(4, 1, 730, Steam.Asset.EType.Emoticon);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1Type1Game1X9, item3Type2Game2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1Type1Game1, item3Type2Game2 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2Type1Game1, item4Type2Game2 };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void MultiGameSingleTypeBadReject() {
			Steam.Asset item1Game1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1Game1X9 = GenerateSteamCommunityItem(1, 9, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Game1 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			Steam.Asset item1Game2 = GenerateSteamCommunityItem(3, 1, 730, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Game2 = GenerateSteamCommunityItem(4, 1, 730, Steam.Asset.EType.TradingCard);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1Game1X9, item1Game2, item2Game2 };

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1Game1, item1Game2 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2Game1, item2Game2 };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void MultiGameSingleTypeNeutralAccept() {
			Steam.Asset item1Game1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1Game1X2 = GenerateSteamCommunityItem(1, 2, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Game1 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			Steam.Asset item1Game2 = GenerateSteamCommunityItem(1, 1, 730, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Game2 = GenerateSteamCommunityItem(2, 1, 730, Steam.Asset.EType.TradingCard);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1Game1X2, item1Game2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1Game1, item1Game2 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2Game1, item2Game2 };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameMultiTypeBadReject() {
			Steam.Asset item1Type1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1Type1X9 = GenerateSteamCommunityItem(1, 9, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Type1 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			Steam.Asset item3Type2 = GenerateSteamCommunityItem(3, 1, 570, Steam.Asset.EType.Emoticon);
			Steam.Asset item3Type2X9 = GenerateSteamCommunityItem(3, 9, 570, Steam.Asset.EType.Emoticon);
			Steam.Asset item4Type2 = GenerateSteamCommunityItem(4, 1, 570, Steam.Asset.EType.Emoticon);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1Type1X9, item3Type2X9, item4Type2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1Type1, item4Type2 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2Type1, item3Type2 };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameMultiTypeNeutralAccept() {
			Steam.Asset item1Type1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1Type1X9 = GenerateSteamCommunityItem(1, 9, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2Type1 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			Steam.Asset item3Type2 = GenerateSteamCommunityItem(3, 1, 570, Steam.Asset.EType.Emoticon);
			Steam.Asset item4Type2 = GenerateSteamCommunityItem(4, 1, 570, Steam.Asset.EType.Emoticon);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1Type1X9, item3Type2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1Type1, item3Type2 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2Type1, item4Type2 };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameQuantityBadReject() {
			Steam.Asset item1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item3 = GenerateSteamCommunityItem(3, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item4X3 = GenerateSteamCommunityItem(4, 3, 570, Steam.Asset.EType.TradingCard);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1, item2, item3 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1, item2, item3 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item4X3 };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameQuantityNeutralAccept() {
			Steam.Asset item1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1X2 = GenerateSteamCommunityItem(1, 2, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item3X2 = GenerateSteamCommunityItem(3, 2, 570, Steam.Asset.EType.TradingCard);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1X2, item2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1, item2 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item3X2 };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeBadReject() {
			Steam.Asset item1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1, item2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2 };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeGoodAccept() {
			Steam.Asset item1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item1X2 = GenerateSteamCommunityItem(1, 2, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1X2 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2 };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeNeutralAccept() {
			Steam.Asset item1 = GenerateSteamCommunityItem(1, 1, 570, Steam.Asset.EType.TradingCard);
			Steam.Asset item2 = GenerateSteamCommunityItem(2, 1, 570, Steam.Asset.EType.TradingCard);

			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { item1 };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { item1 };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { item2 };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		private static bool AcceptsTrade(IReadOnlyCollection<Steam.Asset> inventory, IReadOnlyCollection<Steam.Asset> itemsToGive, IReadOnlyCollection<Steam.Asset> itemsToReceive) {
			Type trading = typeof(ArchiSteamFarm.Trading);
			MethodInfo method = trading.GetMethod("IsTradeNeutralOrBetter", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static);

			if (method == null) {
				throw new ArgumentNullException(nameof(method));
			}

			return (bool) method.Invoke(null, new object[] { inventory, itemsToGive, itemsToReceive });
		}

		private static Steam.Asset GenerateSteamCommunityItem(ulong classID, uint amount, uint realAppID, Steam.Asset.EType type) => new Steam.Asset(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID, type);
	}
}