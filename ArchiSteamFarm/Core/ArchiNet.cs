//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
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
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Core;

internal static class ArchiNet {
	internal static Uri URL => new("https://asf-backend.JustArchi.net");

	internal static async Task<string?> FetchBuildChecksum(Version version, string variant) {
		ArgumentNullException.ThrowIfNull(version);

		if (string.IsNullOrEmpty(variant)) {
			throw new ArgumentNullException(nameof(variant));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		Uri request = new(URL, $"/Api/Checksum/{version}/{variant}");

		ObjectResponse<ChecksumResponse>? response = await ASF.WebBrowser.UrlGetToJsonObject<ChecksumResponse>(request).ConfigureAwait(false);

		if (response?.Content == null) {
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

	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class ListedUser {
		[JsonProperty("items_count", Required = Required.Always)]
		internal readonly ushort ItemsCount;

		internal readonly HashSet<Asset.EType> MatchableTypes = new();

		[JsonProperty("max_trade_hold_duration", Required = Required.Always)]
		internal readonly byte MaxTradeHoldDuration;

		[JsonProperty("steam_id", Required = Required.Always)]
		internal readonly ulong SteamID;

		[JsonProperty("trade_token", Required = Required.Always)]
		internal readonly string TradeToken = "";

		internal float Score => GamesCount / (float) ItemsCount;

#pragma warning disable CS0649 // False positive, it's a field set during json deserialization
		[JsonProperty("games_count", Required = Required.Always)]
		private readonly ushort GamesCount;
#pragma warning restore CS0649 // False positive, it's a field set during json deserialization

		internal bool MatchEverything { get; private set; }

		[JsonProperty("matchable_backgrounds", Required = Required.Always)]
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

		[JsonProperty("matchable_cards", Required = Required.Always)]
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

		[JsonProperty("matchable_emoticons", Required = Required.Always)]
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

		[JsonProperty("matchable_foil_cards", Required = Required.Always)]
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

		[JsonProperty("match_everything", Required = Required.Always)]
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
