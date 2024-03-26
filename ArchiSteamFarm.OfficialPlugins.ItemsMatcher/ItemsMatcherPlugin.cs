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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Composition;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Integration.Callbacks;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

[Export(typeof(IPlugin))]
internal sealed class ItemsMatcherPlugin : OfficialPlugin, IBot, IBotCommand2, IBotIdentity, IBotModules, IBotTradeOfferResults, IBotUserNotifications {
	internal static readonly ConcurrentDictionary<Bot, RemoteCommunication> RemoteCommunications = new();

	[JsonInclude]
	[Required]
	public override string Name => nameof(ItemsMatcherPlugin);

	[JsonInclude]
	[Required]
	public override Version Version => typeof(ItemsMatcherPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		if ((args == null) || (args.Length == 0)) {
			throw new ArgumentNullException(nameof(args));
		}

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		return await Commands.OnBotCommand(bot, access, args, steamID).ConfigureAwait(false);
	}

	public async Task OnBotDestroy(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (RemoteCommunications.TryRemove(bot, out RemoteCommunication? remoteCommunication)) {
			await remoteCommunication.DisposeAsync().ConfigureAwait(false);
		}
	}

	public Task OnBotInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		return Task.CompletedTask;
	}

	public async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		ArgumentNullException.ThrowIfNull(bot);

		if (RemoteCommunications.TryRemove(bot, out RemoteCommunication? remoteCommunication)) {
			await remoteCommunication.DisposeAsync().ConfigureAwait(false);
		}

		remoteCommunication = new RemoteCommunication(bot);

		if (!RemoteCommunications.TryAdd(bot, remoteCommunication)) {
			await remoteCommunication.DisposeAsync().ConfigureAwait(false);
		}
	}

	public Task OnBotTradeOfferResults(Bot bot, IReadOnlyCollection<ParseTradeResult> tradeResults) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((tradeResults == null) || (tradeResults.Count == 0)) {
			throw new ArgumentNullException(nameof(tradeResults));
		}

		// We're interested only in Items notification for Bot that has RemoteCommunication enabled
		if (!RemoteCommunications.TryGetValue(bot, out RemoteCommunication? remoteCommunication) || !tradeResults.Any(tradeResult => tradeResult is { Result: ParseTradeResult.EResult.Accepted, Confirmed: true } && ((tradeResult.ItemsToGive?.Any(item => bot.BotConfig.MatchableTypes.Contains(item.Type)) == true) || (tradeResult.ItemsToReceive?.Any(item => bot.BotConfig.MatchableTypes.Contains(item.Type)) == true)))) {
			return Task.CompletedTask;
		}

		remoteCommunication.OnNewItemsNotification();

		return Task.CompletedTask;
	}

	public Task OnBotUserNotifications(Bot bot, IReadOnlyCollection<UserNotificationsCallback.EUserNotification> newNotifications) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((newNotifications == null) || (newNotifications.Count == 0)) {
			throw new ArgumentNullException(nameof(newNotifications));
		}

		// We're interested only in Items notification for Bot that has RemoteCommunication enabled
		if (!newNotifications.Contains(UserNotificationsCallback.EUserNotification.Items) || !RemoteCommunications.TryGetValue(bot, out RemoteCommunication? remoteCommunication)) {
			return Task.CompletedTask;
		}

		remoteCommunication.OnNewItemsNotification();

		return Task.CompletedTask;
	}

	public override Task OnLoaded() {
		Utilities.WarnAboutIncompleteTranslation(Strings.ResourceManager);

		return Task.CompletedTask;
	}

	public async Task OnSelfPersonaState(Bot bot, SteamFriends.PersonaStateCallback data, string? nickname, string? avatarHash) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!RemoteCommunications.TryGetValue(bot, out RemoteCommunication? remoteCommunication)) {
			// This bot doesn't have RemoteCommunication enabled
			return;
		}

		await remoteCommunication.OnPersonaState(nickname, avatarHash).ConfigureAwait(false);
	}
}
