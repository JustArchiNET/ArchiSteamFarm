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
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Core;

internal static class ArchiNet {
	private static Uri URL => new("https://asf.JustArchi.net");

	internal static async Task<HttpStatusCode?> AnnounceForListing(Bot bot, IReadOnlyCollection<Asset> inventory, IReadOnlyCollection<Asset.EType> acceptedMatchableTypes, string tradeToken, string? nickname = null, string? avatarHash = null) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((inventory == null) || (inventory.Count == 0)) {
			throw new ArgumentNullException(nameof(inventory));
		}

		if ((acceptedMatchableTypes == null) || (acceptedMatchableTypes.Count == 0)) {
			throw new ArgumentNullException(nameof(acceptedMatchableTypes));
		}

		if (string.IsNullOrEmpty(tradeToken)) {
			throw new ArgumentNullException(nameof(tradeToken));
		}

		if (tradeToken.Length != BotConfig.SteamTradeTokenLength) {
			throw new ArgumentOutOfRangeException(nameof(tradeToken));
		}

		Uri request = new(URL, "/Api/Announce");

		Dictionary<string, string> data = new(9, StringComparer.Ordinal) {
			{ "AvatarHash", avatarHash ?? "" },
			{ "GamesCount", inventory.Select(static item => item.RealAppID).Distinct().Count().ToString(CultureInfo.InvariantCulture) },
			{ "Guid", (ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid()).ToString("N") },
			{ "ItemsCount", inventory.Count.ToString(CultureInfo.InvariantCulture) },
			{ "MatchableTypes", JsonConvert.SerializeObject(acceptedMatchableTypes) },
			{ "MatchEverything", bot.BotConfig.TradingPreferences.HasFlag(BotConfig.ETradingPreferences.MatchEverything) ? "1" : "0" },
			{ "Nickname", nickname ?? "" },
			{ "SteamID", bot.SteamID.ToString(CultureInfo.InvariantCulture) },
			{ "TradeToken", tradeToken }
		};

		BasicResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

		return response?.StatusCode;
	}

	internal static async Task<string?> FetchBuildChecksum(Version version, string variant) {
		if (version == null) {
			throw new ArgumentNullException(nameof(version));
		}

		if (string.IsNullOrEmpty(variant)) {
			throw new ArgumentNullException(nameof(variant));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, $"/Api/Checksum/{version}/{variant}");

		ObjectResponse<ChecksumResponse>? response = await ASF.WebBrowser.UrlGetToJsonObject<ChecksumResponse>(request).ConfigureAwait(false);

		if (response == null) {
			return null;
		}

		return response.Content.Checksum ?? "";
	}

	internal static async Task<ImmutableHashSet<ListedUser>?> GetListedUsers(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Uri request = new(URL, "/Api/Bots");

		ObjectResponse<ImmutableHashSet<ListedUser>>? response = await bot.ArchiWebHandler.WebBrowser.UrlGetToJsonObject<ImmutableHashSet<ListedUser>>(request).ConfigureAwait(false);

		return response?.Content;
	}

	internal static async Task<HttpStatusCode?> HeartBeatForListing(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		Uri request = new(URL, "/Api/HeartBeat");

		Dictionary<string, string> data = new(2, StringComparer.Ordinal) {
			{ "Guid", (ASF.GlobalDatabase?.Identifier ?? Guid.NewGuid()).ToString("N") },
			{ "SteamID", bot.SteamID.ToString(CultureInfo.InvariantCulture) }
		};

		BasicResponse? response = await bot.ArchiWebHandler.WebBrowser.UrlPost(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

		return response?.StatusCode;
	}

	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class ListedUser {
#pragma warning disable CS0649 // False positive, it's a field set during json deserialization
		[JsonProperty(PropertyName = "items_count", Required = Required.Always)]
		internal readonly ushort ItemsCount;
#pragma warning restore CS0649 // False positive, it's a field set during json deserialization

		internal readonly HashSet<Asset.EType> MatchableTypes = new();

#pragma warning disable CS0649 // False positive, it's a field set during json deserialization
		[JsonProperty(PropertyName = "steam_id", Required = Required.Always)]
		internal readonly ulong SteamID;
#pragma warning restore CS0649 // False positive, it's a field set during json deserialization

		[JsonProperty(PropertyName = "trade_token", Required = Required.Always)]
		internal readonly string TradeToken = "";

		internal float Score => GamesCount / (float) ItemsCount;

#pragma warning disable CS0649 // False positive, it's a field set during json deserialization
		[JsonProperty(PropertyName = "games_count", Required = Required.Always)]
		private readonly ushort GamesCount;
#pragma warning restore CS0649 // False positive, it's a field set during json deserialization

		internal bool MatchEverything { get; private set; }

		[JsonProperty(PropertyName = "matchable_backgrounds", Required = Required.Always)]
		private byte MatchableBackgroundsNumber {
			set {
				switch (value) {
					case 0:
						MatchableTypes.Remove(Asset.EType.ProfileBackground);

						break;
					case 1:
						MatchableTypes.Add(Asset.EType.ProfileBackground);

						break;
					default:
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

						return;
				}
			}
		}

		[JsonProperty(PropertyName = "matchable_cards", Required = Required.Always)]
		private byte MatchableCardsNumber {
			set {
				switch (value) {
					case 0:
						MatchableTypes.Remove(Asset.EType.TradingCard);

						break;
					case 1:
						MatchableTypes.Add(Asset.EType.TradingCard);

						break;
					default:
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

						return;
				}
			}
		}

		[JsonProperty(PropertyName = "matchable_emoticons", Required = Required.Always)]
		private byte MatchableEmoticonsNumber {
			set {
				switch (value) {
					case 0:
						MatchableTypes.Remove(Asset.EType.Emoticon);

						break;
					case 1:
						MatchableTypes.Add(Asset.EType.Emoticon);

						break;
					default:
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

						return;
				}
			}
		}

		[JsonProperty(PropertyName = "matchable_foil_cards", Required = Required.Always)]
		private byte MatchableFoilCardsNumber {
			set {
				switch (value) {
					case 0:
						MatchableTypes.Remove(Asset.EType.FoilTradingCard);

						break;
					case 1:
						MatchableTypes.Add(Asset.EType.FoilTradingCard);

						break;
					default:
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

						return;
				}
			}
		}

		[JsonProperty(PropertyName = "match_everything", Required = Required.Always)]
		private byte MatchEverythingNumber {
			set {
				switch (value) {
					case 0:
						MatchEverything = false;

						break;
					case 1:
						MatchEverything = true;

						break;
					default:
						ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(value), value));

						return;
				}
			}
		}

		[JsonConstructor]
		private ListedUser() { }
	}

	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	private sealed class ChecksumResponse {
#pragma warning disable CS0649 // False positive, the field is used during json deserialization
		[JsonProperty("checksum", Required = Required.AllowNull)]
		internal readonly string? Checksum;
#pragma warning restore CS0649 // False positive, the field is used during json deserialization

		[JsonConstructor]
		private ChecksumResponse() { }
	}
}
