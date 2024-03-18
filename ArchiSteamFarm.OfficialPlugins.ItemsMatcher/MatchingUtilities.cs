// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using ArchiSteamFarm.Steam.Data;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

internal static class MatchingUtilities {
	internal static (Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> FullState, Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> TradableState) GetDividedInventoryState(IReadOnlyCollection<Asset> inventory) {
		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> fullState = new();
		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> tradableState = new();

		foreach (Asset item in inventory) {
			(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

			if (fullState.TryGetValue(key, out Dictionary<ulong, uint>? fullSet)) {
				fullSet[item.ClassID] = fullSet.GetValueOrDefault(item.ClassID) + item.Amount;
			} else {
				fullState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
			}

			if (!item.Tradable) {
				continue;
			}

			if (tradableState.TryGetValue(key, out Dictionary<ulong, uint>? tradableSet)) {
				tradableSet[item.ClassID] = tradableSet.GetValueOrDefault(item.ClassID) + item.Amount;
			} else {
				tradableState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
			}
		}

		return (fullState, tradableState);
	}

	internal static Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> GetTradableInventoryState(IReadOnlyCollection<Asset> inventory) {
		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		Dictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> tradableState = new();

		foreach (Asset item in inventory.Where(static item => item.Tradable)) {
			(uint RealAppID, EAssetType Type, EAssetRarity Rarity) key = (item.RealAppID, item.Type, item.Rarity);

			if (tradableState.TryGetValue(key, out Dictionary<ulong, uint>? tradableSet)) {
				tradableSet[item.ClassID] = tradableSet.GetValueOrDefault(item.ClassID) + item.Amount;
			} else {
				tradableState[key] = new Dictionary<ulong, uint> { { item.ClassID, item.Amount } };
			}
		}

		return tradableState;
	}

	internal static HashSet<Asset> GetTradableItemsFromInventory(IReadOnlyCollection<Asset> inventory, IReadOnlyDictionary<ulong, uint> classIDs, bool randomize = false) {
		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		if ((classIDs == null) || (classIDs.Count == 0)) {
			throw new ArgumentNullException(nameof(classIDs));
		}

		// We need a copy of classIDs passed since we're going to manipulate them
		Dictionary<ulong, uint> classIDsState = classIDs.ToDictionary();

		HashSet<Asset> result = [];

		IEnumerable<Asset> items = inventory.Where(static item => item.Tradable);

		// Randomization helps to decrease "items no longer available" in regards to sending offers to other users
		if (randomize) {
#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
			items = items.Where(item => classIDsState.ContainsKey(item.ClassID)).OrderBy(static _ => Random.Shared.Next());
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner
		}

		foreach (Asset item in items) {
			if (!classIDsState.TryGetValue(item.ClassID, out uint amount)) {
				continue;
			}

			if (amount >= item.Amount) {
				result.Add(item);

				if (amount > item.Amount) {
					classIDsState[item.ClassID] = amount - item.Amount;
				} else {
					classIDsState.Remove(item.ClassID);

					if (classIDsState.Count == 0) {
						return result;
					}
				}
			} else {
				Asset itemToAdd = item.DeepClone();

				itemToAdd.Amount = amount;

				result.Add(itemToAdd);

				classIDsState.Remove(itemToAdd.ClassID);

				if (classIDsState.Count == 0) {
					return result;
				}
			}
		}

		// If we got here it means we still have classIDs to match
		throw new InvalidOperationException(nameof(classIDs));
	}

	internal static bool IsEmptyForMatching(IReadOnlyDictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> fullState, IReadOnlyDictionary<(uint RealAppID, EAssetType Type, EAssetRarity Rarity), Dictionary<ulong, uint>> tradableState) {
		ArgumentNullException.ThrowIfNull(fullState);
		ArgumentNullException.ThrowIfNull(tradableState);

		foreach (((uint RealAppID, EAssetType Type, EAssetRarity Rarity) set, IReadOnlyDictionary<ulong, uint> state) in tradableState) {
			if (!fullState.TryGetValue(set, out Dictionary<ulong, uint>? fullSet) || (fullSet.Count == 0)) {
				throw new InvalidOperationException(nameof(fullSet));
			}

			if (!IsEmptyForMatching(fullSet, state)) {
				return false;
			}
		}

		// We didn't find any matchable combinations, so this inventory is empty
		return true;
	}

	internal static bool IsEmptyForMatching(IReadOnlyDictionary<ulong, uint> fullSet, IReadOnlyDictionary<ulong, uint> tradableSet) {
		ArgumentNullException.ThrowIfNull(fullSet);
		ArgumentNullException.ThrowIfNull(tradableSet);

		foreach ((ulong classID, uint amount) in tradableSet) {
			switch (amount) {
				case 0:
					// No tradable items, this should never happen, dictionary should not have this key to begin with
					throw new InvalidOperationException(nameof(amount));
				case 1:
					// Single tradable item, can be matchable or not depending on the rest of the inventory
					if (!fullSet.TryGetValue(classID, out uint fullAmount) || (fullAmount == 0)) {
						throw new InvalidOperationException(nameof(fullAmount));
					}

					if (fullAmount > 1) {
						// If we have a single tradable item but more than 1 in total, this is matchable
						return false;
					}

					// A single exclusive tradable item is not matchable, continue
					continue;
				default:
					// Any other combination of tradable items is always matchable
					return false;
			}
		}

		// We didn't find any matchable combinations, so this inventory is empty
		return true;
	}
}
