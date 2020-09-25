//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using NLog;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;

namespace ArchiSteamFarm.NLog {
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	[Target(TargetName)]
	internal sealed class SteamTarget : TargetWithLayout {
		internal const string TargetName = "Steam";

		// This is NLog config property, it must have public get() and set() capabilities
		[PublicAPI]
		public Layout? BotName { get; set; }

		// This is NLog config property, it must have public get() and set() capabilities
		[PublicAPI]
		public ulong ChatGroupID { get; set; }

		// This is NLog config property, it must have public get() and set() capabilities
		[PublicAPI]
		[RequiredParameter]
		public ulong SteamID { get; set; }

		// This parameter-less constructor is intentionally public, as NLog uses it for creating targets
		// It must stay like this as we want to have our targets defined in our NLog.config
		// Keeping date in default layout also doesn't make much sense (Steam offers that), so we remove it by default
		public SteamTarget() => Layout = "${level:uppercase=true}|${logger}|${message}";

		protected override async void Write(LogEventInfo logEvent) {
			if (logEvent == null) {
				throw new ArgumentNullException(nameof(logEvent));
			}

			base.Write(logEvent);

			if ((SteamID == 0) || (Bot.Bots == null) || (Bot.Bots.Count == 0)) {
				return;
			}

			string message = Layout.Render(logEvent);

			if (string.IsNullOrEmpty(message)) {
				return;
			}

			Bot? bot = null;

			string? botName = BotName?.Render(logEvent);

			if (!string.IsNullOrEmpty(botName)) {
				bot = Bot.GetBot(botName!);

				if (bot?.IsConnectedAndLoggedOn != true) {
					return;
				}
			}

			if (ChatGroupID != 0) {
				await SendGroupMessage(message, bot).ConfigureAwait(false);
			} else if ((bot == null) || (bot.SteamID != SteamID)) {
				await SendPrivateMessage(message, bot).ConfigureAwait(false);
			}
		}

		private async Task SendGroupMessage(string message, Bot? bot = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			if (bot == null) {
				bot = Bot.Bots?.Values.FirstOrDefault(targetBot => targetBot.IsConnectedAndLoggedOn);

				if (bot == null) {
					return;
				}
			}

			if (!await bot.SendMessage(ChatGroupID, SteamID, message).ConfigureAwait(false)) {
				bot.ArchiLogger.LogGenericTrace(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
			}
		}

		private async Task SendPrivateMessage(string message, Bot? bot = null) {
			if (string.IsNullOrEmpty(message)) {
				throw new ArgumentNullException(nameof(message));
			}

			if (bot == null) {
				bot = Bot.Bots?.Values.FirstOrDefault(targetBot => targetBot.IsConnectedAndLoggedOn && (targetBot.SteamID != SteamID));

				if (bot == null) {
					return;
				}
			}

			if (!await bot.SendMessage(SteamID, message).ConfigureAwait(false)) {
				bot.ArchiLogger.LogGenericTrace(string.Format(Strings.WarningFailedWithError, nameof(Bot.SendMessage)));
			}
		}
	}
}
