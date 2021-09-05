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
using System.Linq;
using System.Threading;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Web;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Integration {
	internal static class SteamPICSChanges {
		private const byte RefreshTimerInMinutes = 5;

		internal static bool LiveUpdate { get; private set; }

		private static readonly SemaphoreSlim RefreshSemaphore = new(1, 1);
		private static readonly Timer RefreshTimer = new(RefreshChanges);

		private static uint LastChangeNumber;
		private static bool TimerAlreadySet;

		internal static void Init(uint changeNumberToStartFrom) => LastChangeNumber = changeNumberToStartFrom;

		internal static void OnBotLoggedOn() {
			if (TimerAlreadySet) {
				return;
			}

			lock (RefreshSemaphore) {
				if (TimerAlreadySet) {
					return;
				}

				TimerAlreadySet = true;
				RefreshTimer.Change(TimeSpan.Zero, TimeSpan.FromMinutes(RefreshTimerInMinutes));
			}
		}

		private static async void RefreshChanges(object? state = null) {
			if (!await RefreshSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				Bot? refreshBot = null;
				SteamApps.PICSChangesCallback? picsChanges = null;

				for (byte i = 0; (i < WebBrowser.MaxTries) && (picsChanges == null); i++) {
					refreshBot = Bot.Bots?.Values.FirstOrDefault(bot => bot.IsConnectedAndLoggedOn);

					if (refreshBot == null) {
						LiveUpdate = false;

						return;
					}

					try {
						picsChanges = await refreshBot.SteamApps.PICSGetChangesSince(LastChangeNumber, true, true).ToLongRunningTask().ConfigureAwait(false);
					} catch (Exception e) {
						refreshBot.ArchiLogger.LogGenericWarningException(e);
					}
				}

				if ((refreshBot == null) || (picsChanges == null)) {
					LiveUpdate = false;
					ASF.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

					return;
				}

				if (picsChanges.CurrentChangeNumber == picsChanges.LastChangeNumber) {
					LiveUpdate = true;

					return;
				}

				LastChangeNumber = picsChanges.CurrentChangeNumber;

				if (picsChanges.RequiresFullAppUpdate || picsChanges.RequiresFullPackageUpdate) {
					if (ASF.GlobalDatabase != null) {
						await ASF.GlobalDatabase.OnPICSChangesRestart(picsChanges.CurrentChangeNumber).ConfigureAwait(false);
					}

					LiveUpdate = true;

					await PluginsCore.OnPICSChangesRestart(picsChanges.CurrentChangeNumber).ConfigureAwait(false);

					return;
				}

				LiveUpdate = true;

				if (ASF.GlobalDatabase != null) {
					ASF.GlobalDatabase.LastChangeNumber = picsChanges.CurrentChangeNumber;

					if (picsChanges.PackageChanges.Count > 0) {
						await ASF.GlobalDatabase.RefreshPackages(refreshBot, picsChanges.PackageChanges.ToDictionary(package => package.Key, package => package.Value.ChangeNumber)).ConfigureAwait(false);
					}
				}

				await PluginsCore.OnPICSChanges(picsChanges.CurrentChangeNumber, picsChanges.AppChanges, picsChanges.PackageChanges).ConfigureAwait(false);
			} finally {
				RefreshSemaphore.Release();
			}
		}
	}
}
