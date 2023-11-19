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

using System;
using ArchiSteamFarm.Steam.Data;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Data;

internal sealed class AssetForListing : AssetInInventory, IEquatable<AssetForListing> {
	[JsonProperty("i", Required = Required.Always)]
	internal readonly uint Index;

	[JsonProperty("l", Required = Required.Always)]
	internal readonly ulong PreviousAssetID;

	[JsonProperty("z", Required = Required.Always)]
	internal EAssetForListingChangeType ChangeType { get; set; }

	internal AssetForListing(Asset asset, uint index, ulong previousAssetID) : base(asset) {
		ArgumentNullException.ThrowIfNull(asset);

		Index = index;
		PreviousAssetID = previousAssetID;
	}

	public bool Equals(AssetForListing? other) {
		if (ReferenceEquals(null, other)) {
			return false;
		}

		if (ReferenceEquals(this, other)) {
			return true;
		}

		return (Index == other.Index) && (PreviousAssetID == other.PreviousAssetID) && base.Equals(other);
	}

	public override bool Equals(object? obj) {
		if (ReferenceEquals(null, obj)) {
			return false;
		}

		if (ReferenceEquals(this, obj)) {
			return true;
		}

		return obj is AssetInInventory other && Equals(other);
	}

	public override int GetHashCode() {
		HashCode hash = new();

		hash.Add(Index);
		hash.Add(PreviousAssetID);
		hash.Add(AssetID);
		hash.Add(Amount);
		hash.Add(ClassID);
		hash.Add(Rarity);
		hash.Add(RealAppID);
		hash.Add(Tradable);
		hash.Add(Type);

		return hash.ToHashCode();
	}

	[UsedImplicitly]
	public bool ShouldSerializeChangeType() => ChangeType != default(EAssetForListingChangeType);
}
