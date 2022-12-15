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
using System.Collections.Concurrent;
using System.Composition;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.OfficialPlugins.ItemsMatcher.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Storage;
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.ItemsMatcher;

[Export(typeof(IPlugin))]
internal sealed class ItemsMatcherPlugin : OfficialPlugin, IBot, IBotIdentity {
	private static readonly ConcurrentDictionary<Bot, RemoteCommunication> RemoteCommunications = new();

	[JsonProperty]
	public override string Name => nameof(ItemsMatcherPlugin);

	[JsonProperty]
	public override Version Version => typeof(ItemsMatcherPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public async Task OnBotDestroy(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (RemoteCommunications.TryRemove(bot, out RemoteCommunication? remoteCommunications)) {
			await remoteCommunications.DisposeAsync().ConfigureAwait(false);
		}
	}

	public async Task OnBotInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (RemoteCommunications.TryRemove(bot, out RemoteCommunication? remoteCommunications)) {
			await remoteCommunications.DisposeAsync().ConfigureAwait(false);
		}

		if (bot.BotConfig.RemoteCommunication == BotConfig.ERemoteCommunication.None) {
			return;
		}

		RemoteCommunication remoteCommunication = new(bot);

		if (!RemoteCommunications.TryAdd(bot, remoteCommunication)) {
			await remoteCommunication.DisposeAsync().ConfigureAwait(false);
		}
	}

	public override Task OnLoaded() {
		Utilities.WarnAboutIncompleteTranslation(Strings.ResourceManager);

		return Task.CompletedTask;
	}

	public async Task OnSelfPersonaState(Bot bot, SteamFriends.PersonaStateCallback data, string? nickname, string? avatarHash) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!RemoteCommunications.TryGetValue(bot, out RemoteCommunication? remoteCommunication)) {
			return;
		}

		await remoteCommunication.OnPersonaState(nickname, avatarHash).ConfigureAwait(false);
	}
}
