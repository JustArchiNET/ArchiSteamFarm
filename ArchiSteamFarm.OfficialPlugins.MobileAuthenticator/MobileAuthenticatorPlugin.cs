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
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.OfficialPlugins.MobileAuthenticator.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.MobileAuthenticator;

[Export(typeof(IPlugin))]
[SuppressMessage("ReSharper", "MemberCanBeFileLocal")]
internal sealed class MobileAuthenticatorPlugin : OfficialPlugin, IBotCommand2, IBotSteamClient {
	[JsonInclude]
	[Required]
	public override string Name => nameof(MobileAuthenticatorPlugin);

	[JsonInclude]
	[Required]
	public override Version Version => typeof(MobileAuthenticatorPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

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

		return await Commands.OnBotCommand(bot, access, message, args, steamID).ConfigureAwait(false);
	}

	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(callbackManager);

		return Task.CompletedTask;
	}

	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		SteamUnifiedMessages? steamUnifiedMessages = bot.GetHandler<SteamUnifiedMessages>();

		if (steamUnifiedMessages == null) {
			throw new InvalidOperationException(nameof(steamUnifiedMessages));
		}

		return Task.FromResult<IReadOnlyCollection<ClientMsgHandler>?>(new HashSet<ClientMsgHandler>(1) { new MobileAuthenticatorHandler(bot.ArchiLogger, steamUnifiedMessages) });
	}

	public override Task OnLoaded() {
		Utilities.WarnAboutIncompleteTranslation(Strings.ResourceManager);

		return Task.CompletedTask;
	}
}
