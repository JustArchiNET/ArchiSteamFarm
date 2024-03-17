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
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Steam.Data;

namespace ArchiSteamFarm.IPC.Requests;

[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
public sealed class TwoFactorAuthenticationConfirmationsRequest {
	/// <summary>
	///     Specifies the target action, whether we should accept the confirmations (true), or decline them (false).
	/// </summary>
	[JsonInclude]
	[JsonRequired]
	[Required]
	public bool Accept { get; private init; }

	/// <summary>
	///     Specifies IDs of the confirmations that we're supposed to handle. CreatorID of the confirmation is equal to ID of the object that triggered it - e.g. ID of the trade offer, or ID of the market listing. If not provided, or empty array, all confirmation IDs are considered for an action.
	/// </summary>
	[JsonDisallowNull]
	[JsonInclude]
	public ImmutableHashSet<ulong> AcceptedCreatorIDs { get; private init; } = [];

	/// <summary>
	///     Specifies the type of confirmations to handle. If not provided, all confirmation types are considered for an action.
	/// </summary>
	[JsonInclude]
	public Confirmation.EConfirmationType? AcceptedType { get; private init; }

	/// <summary>
	///     A helper property which works the same as <see cref="AcceptedCreatorIDs" /> but with values written as strings - for javascript compatibility purposes. Use either this one, or <see cref="AcceptedCreatorIDs" />, not both.
	/// </summary>
	[JsonDisallowNull]
	[JsonInclude]
	[JsonPropertyName($"{SharedInfo.UlongCompatibilityStringPrefix}{nameof(AcceptedCreatorIDs)}")]
	public ImmutableHashSet<string> SAcceptedCreatorIDs {
		get => AcceptedCreatorIDs.Select(static creatorID => creatorID.ToString(CultureInfo.InvariantCulture)).ToImmutableHashSet(StringComparer.Ordinal);

		private init {
			ArgumentNullException.ThrowIfNull(value);

			HashSet<ulong> acceptedCreatorIDs = [];

			foreach (string creatorIDText in value) {
				if (!ulong.TryParse(creatorIDText, out ulong creatorID) || (creatorID == 0)) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(SAcceptedCreatorIDs)));

					return;
				}

				acceptedCreatorIDs.Add(creatorID);
			}

			AcceptedCreatorIDs = acceptedCreatorIDs.ToImmutableHashSet();
		}
	}

	/// <summary>
	///     Specifies whether we should wait for the confirmations to arrive, in case they're not available immediately. This option makes sense only if <see cref="AcceptedCreatorIDs" /> is specified as well, and in this case ASF will add a few more tries if needed to ensure that all specified IDs are handled. Useful if confirmations are generated with a delay on Steam network side, which happens fairly often.
	/// </summary>
	[JsonInclude]
	public bool WaitIfNeeded { get; private init; }

	[JsonConstructor]
	private TwoFactorAuthenticationConfirmationsRequest() { }
}
