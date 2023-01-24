//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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

[TestClass]
public sealed class Trading {
	[TestMethod]
	public void ExploitingNewSetsIsFairButNotNeutral() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 40),
			CreateItem(2, 10),
			CreateItem(3, 10)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(2, 5),
			CreateItem(3, 5)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(1, 9),
			CreateItem(4)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void MismatchRarityIsNotFair() {
		HashSet<Asset> itemsToGive = new() { CreateItem(1, rarity: Asset.ERarity.Rare) };
		HashSet<Asset> itemsToReceive = new() { CreateItem(2) };

		Assert.IsFalse(IsFairExchange(itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void MismatchRealAppIDsIsNotFair() {
		HashSet<Asset> itemsToGive = new() { CreateItem(1, realAppID: 570) };
		HashSet<Asset> itemsToReceive = new() { CreateItem(2) };

		Assert.IsFalse(IsFairExchange(itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void MismatchTypesIsNotFair() {
		HashSet<Asset> itemsToGive = new() { CreateItem(1, type: Asset.EType.Emoticon) };
		HashSet<Asset> itemsToReceive = new() { CreateItem(2) };

		Assert.IsFalse(IsFairExchange(itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void MultiGameMultiTypeBadReject() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 9),
			CreateItem(3, 9, 730, Asset.EType.Emoticon),
			CreateItem(4, realAppID: 730, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(4, realAppID: 730, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(2),
			CreateItem(3, realAppID: 730, type: Asset.EType.Emoticon)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void MultiGameMultiTypeNeutralAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 9),
			CreateItem(3, realAppID: 730, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(3, realAppID: 730, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(2),
			CreateItem(4, realAppID: 730, type: Asset.EType.Emoticon)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void MultiGameSingleTypeBadReject() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 9),
			CreateItem(3, realAppID: 730),
			CreateItem(4, realAppID: 730)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(3, realAppID: 730)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(2),
			CreateItem(4, realAppID: 730)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void MultiGameSingleTypeNeutralAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 2),
			CreateItem(3, realAppID: 730)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(3, realAppID: 730)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(2),
			CreateItem(4, realAppID: 730)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameAbrynosWasWrongNeutralAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1),
			CreateItem(2, 2),
			CreateItem(3),
			CreateItem(4),
			CreateItem(5)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(2)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(3)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameDonationAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(2),
			CreateItem(3, type: Asset.EType.SteamGems)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameMultiTypeBadReject() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 9),
			CreateItem(3, 9, type: Asset.EType.Emoticon),
			CreateItem(4, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(4, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(2),
			CreateItem(3, type: Asset.EType.Emoticon)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameMultiTypeNeutralAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 9),
			CreateItem(3, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(3, type: Asset.EType.Emoticon)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(2),
			CreateItem(4, type: Asset.EType.Emoticon)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameQuantityBadReject() {
		HashSet<Asset> inventory = new() {
			CreateItem(1),
			CreateItem(2),
			CreateItem(3)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(2),
			CreateItem(3)
		};

		HashSet<Asset> itemsToReceive = new() { CreateItem(4, 3) };

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameQuantityBadReject2() {
		HashSet<Asset> inventory = new() {
			CreateItem(1),
			CreateItem(2, 2)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(2, 2)
		};

		HashSet<Asset> itemsToReceive = new() { CreateItem(3, 3) };

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameQuantityNeutralAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 2),
			CreateItem(2)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(1),
			CreateItem(2)
		};

		HashSet<Asset> itemsToReceive = new() { CreateItem(3, 2) };

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameSingleTypeBadReject() {
		HashSet<Asset> inventory = new() {
			CreateItem(1),
			CreateItem(2)
		};

		HashSet<Asset> itemsToGive = new() { CreateItem(1) };
		HashSet<Asset> itemsToReceive = new() { CreateItem(2) };

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameSingleTypeBadWithOverpayingReject() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 2),
			CreateItem(2, 2),
			CreateItem(3, 2)
		};

		HashSet<Asset> itemsToGive = new() { CreateItem(2) };

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(1),
			CreateItem(3)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameSingleTypeBigDifferenceAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1),
			CreateItem(2, 5),
			CreateItem(3)
		};

		HashSet<Asset> itemsToGive = new() { CreateItem(2) };
		HashSet<Asset> itemsToReceive = new() { CreateItem(3) };

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameSingleTypeBigDifferenceReject() {
		HashSet<Asset> inventory = new() {
			CreateItem(1),
			CreateItem(2, 2),
			CreateItem(3, 2),
			CreateItem(4, 3),
			CreateItem(5, 10)
		};

		HashSet<Asset> itemsToGive = new() {
			CreateItem(2),
			CreateItem(5)
		};

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(3),
			CreateItem(4)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsFalse(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameSingleTypeGoodAccept() {
		HashSet<Asset> inventory = new() { CreateItem(1, 2) };
		HashSet<Asset> itemsToGive = new() { CreateItem(1) };
		HashSet<Asset> itemsToReceive = new() { CreateItem(2) };

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameSingleTypeNeutralAccept() {
		HashSet<Asset> inventory = new() { CreateItem(1) };
		HashSet<Asset> itemsToGive = new() { CreateItem(1) };
		HashSet<Asset> itemsToReceive = new() { CreateItem(2) };

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	[TestMethod]
	public void SingleGameSingleTypeNeutralWithOverpayingAccept() {
		HashSet<Asset> inventory = new() {
			CreateItem(1, 2),
			CreateItem(2, 2)
		};

		HashSet<Asset> itemsToGive = new() { CreateItem(2) };

		HashSet<Asset> itemsToReceive = new() {
			CreateItem(1),
			CreateItem(3)
		};

		Assert.IsTrue(IsFairExchange(itemsToGive, itemsToReceive));
		Assert.IsTrue(IsTradeNeutralOrBetter(inventory, itemsToGive, itemsToReceive));
	}

	private static Asset CreateItem(ulong classID, uint amount = 1, uint realAppID = Asset.SteamAppID, Asset.EType type = Asset.EType.TradingCard, Asset.ERarity rarity = Asset.ERarity.Common) => new(Asset.SteamAppID, Asset.SteamCommunityContextID, classID, amount, realAppID: realAppID, type: type, rarity: rarity);
}
