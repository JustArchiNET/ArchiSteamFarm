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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Composition;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper {
	[Export(typeof(IPlugin))]
	internal sealed class SteamTokenDumperPlugin : OfficialPlugin, IASF, IBot, IBotSteamClient, ISteamPICSChanges {
		private static readonly ConcurrentDictionary<Bot, IDisposable> BotSubscriptions = new ConcurrentDictionary<Bot, IDisposable>();
		private static readonly ConcurrentDictionary<Bot, (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer)> BotSynchronizations = new ConcurrentDictionary<Bot, (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer)>();
		private static readonly SemaphoreSlim SubmissionSemaphore = new SemaphoreSlim(1, 1);
		private static readonly Timer SubmissionTimer = new Timer(async e => await SubmitData().ConfigureAwait(false));

		private static GlobalCache? GlobalCache;

		[JsonProperty]
		private static bool IsEnabled;

		[JsonProperty]
		public override string Name => nameof(SteamTokenDumperPlugin);

		[JsonProperty]
		public override Version Version => typeof(SteamTokenDumperPlugin).Assembly.GetName().Version ?? throw new ArgumentNullException(nameof(Version));

		public Task<uint> GetPreferredChangeNumberToStartFrom() => Task.FromResult(IsEnabled ? GlobalCache?.LastChangeNumber ?? 0 : 0);

		public void OnASFInit(IReadOnlyDictionary<string, JToken>? additionalConfigProperties = null) {
			if (!SharedInfo.HasValidToken) {
				ASF.ArchiLogger.LogGenericError($"{Name} has been disabled due to missing build token.");

				return;
			}

			bool enabled = false;

			if (additionalConfigProperties != null) {
				foreach ((string configProperty, JToken configValue) in additionalConfigProperties) {
					try {
						if (configProperty == nameof(GlobalConfigExtension.SteamTokenDumperPluginEnabled)) {
							enabled = configValue.Value<bool>();
						}
					} catch (Exception e) {
						ASF.ArchiLogger.LogGenericException(e);

						break;
					}
				}
			}

			IsEnabled = enabled;

			if (!enabled) {
				ASF.ArchiLogger.LogGenericInfo($"{Name} is currently disabled. If you'd like to help SteamDB in data submission, check out our wiki for {nameof(SteamTokenDumperPlugin)}.");

				return;
			}

			GlobalCache ??= GlobalCache.Load().Result;

			TimeSpan startIn = TimeSpan.FromMinutes(Utilities.RandomNext(SharedInfo.MinimumMinutesBeforeFirstUpload, SharedInfo.MaximumMinutesBeforeFirstUpload));

			lock (SubmissionTimer) {
				SubmissionTimer.Change(startIn, TimeSpan.FromHours(SharedInfo.MinimumHoursBetweenUploads));
			}

			ASF.ArchiLogger.LogGenericInfo($"{Name} has been initialized successfully, thank you for your help. The first submission will happen in approximately {startIn.ToHumanReadable()} from now.");
		}

		public async void OnBotDestroy(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			if (BotSubscriptions.TryRemove(bot, out IDisposable? subscription)) {
				subscription.Dispose();
			}

			if (BotSynchronizations.TryRemove(bot, out (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer) synchronization)) {
				synchronization.RefreshSemaphore.Dispose();

				await synchronization.RefreshTimer.DisposeAsync().ConfigureAwait(false);
			}
		}

		public async void OnBotInit(Bot bot) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			if (!IsEnabled) {
				return;
			}

			SemaphoreSlim refreshSemaphore = new SemaphoreSlim(1, 1);
			Timer refreshTimer = new Timer(async e => await Refresh(bot).ConfigureAwait(false));

			if (!BotSynchronizations.TryAdd(bot, (refreshSemaphore, refreshTimer))) {
				refreshSemaphore.Dispose();

				await refreshTimer.DisposeAsync().ConfigureAwait(false);
			}
		}

		public void OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
			if ((bot == null) || (callbackManager == null)) {
				throw new ArgumentNullException(nameof(bot) + " || " + nameof(callbackManager));
			}

			if (BotSubscriptions.TryRemove(bot, out IDisposable? subscription)) {
				subscription.Dispose();
			}

			if (!IsEnabled) {
				return;
			}

			subscription = callbackManager.Subscribe<SteamApps.LicenseListCallback>(callback => OnLicenseList(bot, callback));

			if (!BotSubscriptions.TryAdd(bot, subscription)) {
				subscription.Dispose();
			}
		}

		public IReadOnlyCollection<ClientMsgHandler>? OnBotSteamHandlersInit(Bot bot) => null;

		public override void OnLoaded() { }

		public async void OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if ((currentChangeNumber == 0) || (appChanges == null) || (packageChanges == null)) {
				throw new ArgumentNullException(nameof(currentChangeNumber) + " || " + nameof(appChanges) + " || " + nameof(packageChanges));
			}

			if (!IsEnabled) {
				return;
			}

			if (GlobalCache == null) {
				throw new ArgumentNullException(nameof(GlobalCache));
			}

			await GlobalCache.OnPICSChanges(currentChangeNumber, appChanges).ConfigureAwait(false);
		}

		public async void OnPICSChangesRestart(uint currentChangeNumber) {
			if (currentChangeNumber == 0) {
				throw new ArgumentNullException(nameof(currentChangeNumber));
			}

			if (!IsEnabled) {
				return;
			}

			if (GlobalCache == null) {
				throw new ArgumentNullException(nameof(GlobalCache));
			}

			await GlobalCache.OnPICSChangesRestart(currentChangeNumber).ConfigureAwait(false);
		}

		private static async void OnLicenseList(Bot bot, SteamApps.LicenseListCallback callback) {
			if ((bot == null) || (callback == null)) {
				throw new ArgumentNullException(nameof(callback));
			}

			if (!IsEnabled) {
				return;
			}

			if (GlobalCache == null) {
				throw new ArgumentNullException(nameof(GlobalCache));
			}

			Dictionary<uint, ulong> packageTokens = callback.LicenseList.GroupBy(license => license.PackageID).ToDictionary(group => group.Key, group => group.OrderByDescending(license => license.TimeCreated).First().AccessToken);

			await GlobalCache.UpdatePackageTokens(packageTokens).ConfigureAwait(false);
			await Refresh(bot, packageTokens.Keys).ConfigureAwait(false);
		}

		private static async Task Refresh(Bot bot, IReadOnlyCollection<uint>? packageIDs = null) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			if (!IsEnabled) {
				return;
			}

			if ((GlobalCache == null) || (ASF.GlobalDatabase == null)) {
				throw new ArgumentNullException(nameof(GlobalCache) + " || " + nameof(ASF.GlobalDatabase));
			}

			if (!BotSynchronizations.TryGetValue(bot, out (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer) synchronization)) {
				throw new ArgumentNullException(nameof(synchronization));
			}

			if (!await synchronization.RefreshSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				if (!bot.IsConnectedAndLoggedOn) {
					return;
				}

				packageIDs ??= bot.OwnedPackageIDsReadOnly;

				HashSet<uint> appIDsToRefresh = new HashSet<uint>();

				foreach (uint packageID in packageIDs) {
					if (!ASF.GlobalDatabase.PackagesDataReadOnly.TryGetValue(packageID, out (uint ChangeNumber, HashSet<uint>? AppIDs) packageData) || (packageData.AppIDs == null)) {
						// ASF might not have the package info for us at the moment, we'll retry later
						continue;
					}

					appIDsToRefresh.UnionWith(packageData.AppIDs.Where(appID => GlobalCache.ShouldRefreshAppInfo(appID)));
				}

				if (appIDsToRefresh.Count == 0) {
					bot.ArchiLogger.LogGenericDebug($"There are no apps to refresh for {bot.BotName}.");

					return;
				}

				bot.ArchiLogger.LogGenericInfo($"Retrieving a total of {appIDsToRefresh.Count} app access tokens...");

				HashSet<uint> appIDsThisRound = new HashSet<uint>(Math.Min(appIDsToRefresh.Count, SharedInfo.AppInfosPerSingleRequest));

				using (HashSet<uint>.Enumerator enumerator = appIDsToRefresh.GetEnumerator()) {
					while (true) {
						while ((appIDsThisRound.Count < SharedInfo.AppInfosPerSingleRequest) && enumerator.MoveNext()) {
							appIDsThisRound.Add(enumerator.Current);
						}

						if (appIDsThisRound.Count == 0) {
							break;
						}

						if (!bot.IsConnectedAndLoggedOn) {
							return;
						}

						bot.ArchiLogger.LogGenericInfo($"Retrieving {appIDsThisRound.Count} app access tokens...");

						SteamApps.PICSTokensCallback response;

						try {
							response = await bot.SteamApps.PICSGetAccessTokens(appIDsThisRound, Enumerable.Empty<uint>()).ToLongRunningTask().ConfigureAwait(false);
						} catch (Exception e) {
							bot.ArchiLogger.LogGenericWarningException(e);

							return;
						}

						if (response == null) {
							bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(response)));

							return;
						}

						bot.ArchiLogger.LogGenericInfo($"Finished retrieving {appIDsThisRound.Count} app access tokens.");

						appIDsThisRound.Clear();

						await GlobalCache.UpdateAppTokens(response.AppTokens, response.AppTokensDenied).ConfigureAwait(false);
					}
				}

				bot.ArchiLogger.LogGenericInfo($"Finished retrieving a total of {appIDsToRefresh.Count} app access tokens.");
				bot.ArchiLogger.LogGenericInfo($"Retrieving all depots for a total of {appIDsToRefresh.Count} apps...");

				appIDsThisRound.Clear();

				using (HashSet<uint>.Enumerator enumerator = appIDsToRefresh.GetEnumerator()) {
					while (true) {
						while ((appIDsThisRound.Count < SharedInfo.AppInfosPerSingleRequest) && enumerator.MoveNext()) {
							appIDsThisRound.Add(enumerator.Current);
						}

						if (appIDsThisRound.Count == 0) {
							break;
						}

						if (!bot.IsConnectedAndLoggedOn) {
							return;
						}

						bot.ArchiLogger.LogGenericInfo($"Retrieving {appIDsThisRound.Count} app infos...");

						AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet response;

						try {
							response = await bot.SteamApps.PICSGetProductInfo(appIDsThisRound.Select(appID => new SteamApps.PICSRequest { ID = appID, AccessToken = GlobalCache.GetAppToken(appID), Public = false }), Enumerable.Empty<SteamApps.PICSRequest>()).ToLongRunningTask().ConfigureAwait(false);
						} catch (Exception e) {
							bot.ArchiLogger.LogGenericWarningException(e);

							return;
						}

						if (response.Results == null) {
							bot.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningFailedWithError, nameof(response.Results)));

							return;
						}

						bot.ArchiLogger.LogGenericInfo($"Finished retrieving {appIDsThisRound.Count} app infos.");

						appIDsThisRound.Clear();

						Dictionary<uint, uint> appChangeNumbers = new Dictionary<uint, uint>();

						HashSet<Task<SteamApps.DepotKeyCallback>> depotTasks = new HashSet<Task<SteamApps.DepotKeyCallback>>();

						foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo app in response.Results.SelectMany(result => result.Apps.Values)) {
							appChangeNumbers[app.ID] = app.ChangeNumber;

							if (GlobalCache.ShouldRefreshDepotKey(app.ID)) {
								depotTasks.Add(bot.SteamApps.GetDepotDecryptionKey(app.ID, app.ID).ToLongRunningTask());
							}

							foreach (KeyValue depot in app.KeyValues["depots"].Children) {
								if (uint.TryParse(depot.Name, out uint depotID) && GlobalCache.ShouldRefreshDepotKey(depotID)) {
									depotTasks.Add(bot.SteamApps.GetDepotDecryptionKey(depotID, app.ID).ToLongRunningTask());
								}
							}
						}

						await GlobalCache.UpdateAppChangeNumbers(appChangeNumbers).ConfigureAwait(false);

						if (depotTasks.Count > 0) {
							bot.ArchiLogger.LogGenericInfo($"Retrieving {depotTasks.Count} depot keys...");

							IList<SteamApps.DepotKeyCallback> results;

							try {
								results = await Utilities.InParallel(depotTasks).ConfigureAwait(false);
							} catch (Exception e) {
								bot.ArchiLogger.LogGenericWarningException(e);

								return;
							}

							bot.ArchiLogger.LogGenericInfo($"Finished retrieving {depotTasks.Count} depot keys.");

							await GlobalCache.UpdateDepotKeys(results).ConfigureAwait(false);
						}
					}
				}

				bot.ArchiLogger.LogGenericInfo($"Finished retrieving all depot keys for a total of {appIDsToRefresh.Count} apps.");
			} finally {
				TimeSpan timeSpan = TimeSpan.FromHours(SharedInfo.MaximumHoursBetweenRefresh);

				synchronization.RefreshTimer.Change(timeSpan, timeSpan);
				synchronization.RefreshSemaphore.Release();
			}
		}

		private static async Task SubmitData() {
			if (Bot.Bots == null) {
				throw new ArgumentNullException(nameof(Bot.Bots));
			}

			const string request = SharedInfo.ServerURL + "/submit";

			if (!IsEnabled) {
				return;
			}

			if ((ASF.GlobalConfig == null) || (ASF.WebBrowser == null) || (GlobalCache == null)) {
				throw new ArgumentNullException(nameof(ASF.GlobalConfig) + " || " + nameof(ASF.WebBrowser) + " || " + nameof(GlobalCache));
			}

			if (!await SubmissionSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				ASF.ArchiLogger.LogGenericDebug($"Skipped {nameof(SubmitData)} trigger because there is already one in progress.");

				return;
			}

			try {
				Dictionary<uint, ulong> appTokens = GlobalCache.GetAppTokensForSubmission();
				Dictionary<uint, ulong> packageTokens = GlobalCache.GetPackageTokensForSubmission();
				Dictionary<uint, string> depotKeys = GlobalCache.GetDepotKeysForSubmission();

				if ((appTokens.Count == 0) && (packageTokens.Count == 0) && (depotKeys.Count == 0)) {
					ASF.ArchiLogger.LogGenericInfo("There is no new data to submit, everything up-to-date.");

					return;
				}

				ulong contributorSteamID = (ASF.GlobalConfig.SteamOwnerID > 0) && new SteamID(ASF.GlobalConfig.SteamOwnerID).IsIndividualAccount ? ASF.GlobalConfig.SteamOwnerID : Bot.Bots.Values.Where(bot => bot.SteamID > 0).OrderByDescending(bot => bot.OwnedPackageIDsReadOnly.Count).FirstOrDefault()?.SteamID ?? 0;

				if (contributorSteamID == 0) {
					ASF.ArchiLogger.LogGenericError($"Skipped {nameof(SubmitData)} trigger because there is no valid steamID we could classify as a contributor. Consider setting up {nameof(ASF.GlobalConfig.SteamOwnerID)} property.");

					return;
				}

				RequestData requestData = new RequestData(contributorSteamID, appTokens, packageTokens, depotKeys);

				ASF.ArchiLogger.LogGenericInfo($"Submitting registered apps/subs/depots: {appTokens.Count}/{packageTokens.Count}/{depotKeys.Count}...");

				WebBrowser.ObjectResponse<ResponseData>? response = await ASF.WebBrowser.UrlPostToJsonObject<ResponseData, RequestData>(request, data: requestData, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

				if ((response?.Content?.Data == null) || response.StatusCode.IsClientErrorCode()) {
					ASF.ArchiLogger.LogGenericWarning(Strings.WarningFailed);

#if NETFRAMEWORK
					if (response?.StatusCode == (HttpStatusCode) 429) {
#else
					if (response?.StatusCode == HttpStatusCode.TooManyRequests) {
#endif
						TimeSpan startIn = TimeSpan.FromMinutes(Utilities.RandomNext(SharedInfo.MinimumMinutesBeforeFirstUpload, SharedInfo.MaximumMinutesBeforeFirstUpload));

						lock (SubmissionTimer) {
							SubmissionTimer.Change(startIn, TimeSpan.FromHours(SharedInfo.MinimumHoursBetweenUploads));
						}

						ASF.ArchiLogger.LogGenericInfo($"The submission will happen in approximately {startIn.ToHumanReadable()} from now.");
					}

					return;
				}

				ASF.ArchiLogger.LogGenericInfo($"Data successfully submitted. Newly registered apps/subs/depots: {response.Content.Data.NewAppsCount}/{response.Content.Data.NewSubsCount}/{response.Content.Data.NewDepotsCount}.");

				await GlobalCache.UpdateSubmittedData(appTokens.Keys, packageTokens.Keys, depotKeys.Keys).ConfigureAwait(false);
			} finally {
				SubmissionSemaphore.Release();
			}
		}
	}
}
