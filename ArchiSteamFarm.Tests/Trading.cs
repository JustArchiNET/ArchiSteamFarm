//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

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
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 9),
				CreateItem(3, 9, 730, Steam.Asset.EType.Emoticon),
				CreateItem(4, realAppID: 730, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(4, realAppID: 730, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(2),
				CreateItem(3, realAppID: 730, type: Steam.Asset.EType.Emoticon)
			};

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void MultiGameMultiTypeNeutralAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 9),
				CreateItem(3, realAppID: 730, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(3, realAppID: 730, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(2),
				CreateItem(4, realAppID: 730, type: Steam.Asset.EType.Emoticon)
			};

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void MultiGameSingleTypeBadReject() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 9),
				CreateItem(3, realAppID: 730),
				CreateItem(4, realAppID: 730)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(3, realAppID: 730)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(2),
				CreateItem(4, realAppID: 730)
			};

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void MultiGameSingleTypeNeutralAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 2),
				CreateItem(3, realAppID: 730)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(3, realAppID: 730)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(2),
				CreateItem(4, realAppID: 730)
			};

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameMultiTypeBadReject() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 9),
				CreateItem(3, 9, type: Steam.Asset.EType.Emoticon),
				CreateItem(4, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(4, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(2),
				CreateItem(3, type: Steam.Asset.EType.Emoticon)
			};

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameMultiTypeNeutralAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 9),
				CreateItem(3, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(3, type: Steam.Asset.EType.Emoticon)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(2),
				CreateItem(4, type: Steam.Asset.EType.Emoticon)
			};

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameQuantityBadReject() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2),
				CreateItem(3)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2),
				CreateItem(3)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { CreateItem(4, 3) };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameQuantityBadReject2() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2, 2)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2, 2)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { CreateItem(3, 3) };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameQuantityNeutralAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 2),
				CreateItem(2)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { CreateItem(3, 2) };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeBadReject() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { CreateItem(1) };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { CreateItem(2) };

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeBadWithOverpayingReject() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 2),
				CreateItem(2, 2),
				CreateItem(3, 2)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { CreateItem(2) };

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(3)
			};

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeBigDifferenceAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2, 5),
				CreateItem(3)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { CreateItem(2) };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { CreateItem(3) };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeBigDifferenceReject() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(2, 2),
				CreateItem(3, 2),
				CreateItem(4, 3),
				CreateItem(5, 10)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> {
				CreateItem(2),
				CreateItem(5)
			};

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(3),
				CreateItem(4)
			};

			Assert.IsFalse(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeGoodAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { CreateItem(1, 2) };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { CreateItem(1) };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { CreateItem(2) };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeNeutralAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> { CreateItem(1) };
			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { CreateItem(1) };
			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> { CreateItem(2) };

			Assert.IsTrue(AcceptsTrade(inventory, itemsToGive, itemsToReceive));
		}

		[TestMethod]
		public void SingleGameSingleTypeNeutralWithOverpayingAccept() {
			HashSet<Steam.Asset> inventory = new HashSet<Steam.Asset> {
				CreateItem(1, 2),
				CreateItem(2, 2)
			};

			HashSet<Steam.Asset> itemsToGive = new HashSet<Steam.Asset> { CreateItem(2) };

			HashSet<Steam.Asset> itemsToReceive = new HashSet<Steam.Asset> {
				CreateItem(1),
				CreateItem(3)
			};

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

		private static Steam.Asset CreateItem(ulong classID, uint amount = 1, uint realAppID = Steam.Asset.SteamAppID, Steam.Asset.EType type = Steam.Asset.EType.TradingCard) => new Steam.Asset(Steam.Asset.SteamAppID, Steam.Asset.SteamCommunityContextID, classID, amount, realAppID, type);
	}
}
