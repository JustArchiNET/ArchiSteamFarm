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

#if NETFRAMEWORK
using ArchiSteamFarm.Compatibility;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.OfficialPlugins.SteamTokenDumper.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper {
	[Export(typeof(IPlugin))]
	internal sealed class SteamTokenDumperPlugin : OfficialPlugin, IASF, IBot, IBotSteamClient, ISteamPICSChanges {
		[JsonProperty]
		internal static SteamTokenDumperConfig? Config { get; private set; }

		private static readonly ConcurrentDictionary<Bot, IDisposable> BotSubscriptions = new();
		private static readonly ConcurrentDictionary<Bot, (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer)> BotSynchronizations = new();
		private static readonly SemaphoreSlim SubmissionSemaphore = new(1, 1);
		private static readonly Timer SubmissionTimer = new(SubmitData);

		private static GlobalCache? GlobalCache;

		[JsonProperty]
		public override string Name => nameof(SteamTokenDumperPlugin);

		[JsonProperty]
		public override Version Version => typeof(SteamTokenDumperPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

		public Task<uint> GetPreferredChangeNumberToStartFrom() => Task.FromResult(Config?.Enabled == true ? GlobalCache?.LastChangeNumber ?? 0 : 0);

		public async void OnASFInit(IReadOnlyDictionary<string, JToken>? additionalConfigProperties = null) {
			if (!SharedInfo.HasValidToken) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.PluginDisabledMissingBuildToken, nameof(SteamTokenDumperPlugin)));

				return;
			}

			bool isEnabled = false;
			SteamTokenDumperConfig? config = null;

			if (additionalConfigProperties != null) {
				foreach ((string configProperty, JToken configValue) in additionalConfigProperties) {
					try {
						switch (configProperty) {
							case nameof(GlobalConfigExtension.SteamTokenDumperPlugin):
								config = configValue.ToObject<SteamTokenDumperConfig>();

								break;
							case nameof(GlobalConfigExtension.SteamTokenDumperPluginEnabled):
								isEnabled = configValue.Value<bool>();

								break;
						}
					} catch (Exception e) {
						ASF.ArchiLogger.LogGenericException(e);
						ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.PluginDisabledInConfig, nameof(SteamTokenDumperPlugin)));

						return;
					}
				}
			}

			config ??= new SteamTokenDumperConfig();

			if (isEnabled) {
				config.Enabled = true;
			}

			Config = config;

			if (!config.Enabled) {
				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginDisabledInConfig, nameof(SteamTokenDumperPlugin)));

				return;
			}

			if (!config.SecretAppIDs.IsEmpty) {
				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginSecretListInitialized, nameof(config.SecretAppIDs), string.Join(", ", config.SecretAppIDs)));
			}

			if (!config.SecretPackageIDs.IsEmpty) {
				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginSecretListInitialized, nameof(config.SecretPackageIDs), string.Join(", ", config.SecretPackageIDs)));
			}

			if (!config.SecretDepotIDs.IsEmpty) {
				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginSecretListInitialized, nameof(config.SecretDepotIDs), string.Join(", ", config.SecretDepotIDs)));
			}

			if (GlobalCache == null) {
				GlobalCache? globalCache = await GlobalCache.Load().ConfigureAwait(false);

				if (globalCache == null) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.FileCouldNotBeLoadedFreshInit, nameof(GlobalCache)));

					GlobalCache = new GlobalCache();
				} else {
					GlobalCache = globalCache;
				}
			}

			TimeSpan startIn = TimeSpan.FromMinutes(Utilities.RandomNext(SharedInfo.MinimumMinutesBeforeFirstUpload, SharedInfo.MaximumMinutesBeforeFirstUpload));

			lock (SubmissionSemaphore) {
				SubmissionTimer.Change(startIn, TimeSpan.FromHours(SharedInfo.MinimumHoursBetweenUploads));
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginInitializedAndEnabled, nameof(SteamTokenDumperPlugin), startIn.ToHumanReadable()));
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

			if (Config is not { Enabled: true }) {
				return;
			}

			SemaphoreSlim refreshSemaphore = new(1, 1);
			Timer refreshTimer = new(OnBotRefreshTimer, bot, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

			if (!BotSynchronizations.TryAdd(bot, (refreshSemaphore, refreshTimer))) {
				refreshSemaphore.Dispose();

				await refreshTimer.DisposeAsync().ConfigureAwait(false);
			}
		}

		public void OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			if (callbackManager == null) {
				throw new ArgumentNullException(nameof(callbackManager));
			}

			if (BotSubscriptions.TryRemove(bot, out IDisposable? subscription)) {
				subscription.Dispose();
			}

			if (Config is not { Enabled: true }) {
				return;
			}

			subscription = callbackManager.Subscribe<SteamApps.LicenseListCallback>(callback => OnLicenseList(bot, callback));

			if (!BotSubscriptions.TryAdd(bot, subscription)) {
				subscription.Dispose();
			}
		}

		public IReadOnlyCollection<ClientMsgHandler>? OnBotSteamHandlersInit(Bot bot) => null;

		public override void OnLoaded() { }

		public void OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
			if (currentChangeNumber == 0) {
				throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
			}

			if (appChanges == null) {
				throw new ArgumentNullException(nameof(appChanges));
			}

			if (packageChanges == null) {
				throw new ArgumentNullException(nameof(packageChanges));
			}

			if (Config is not { Enabled: true }) {
				return;
			}

			if (GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			GlobalCache.OnPICSChanges(currentChangeNumber, appChanges);
		}

		public void OnPICSChangesRestart(uint currentChangeNumber) {
			if (currentChangeNumber == 0) {
				throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
			}

			if (Config is not { Enabled: true }) {
				return;
			}

			if (GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			GlobalCache.OnPICSChangesRestart(currentChangeNumber);
		}

		private static async void OnBotRefreshTimer(object? state) {
			if (state is not Bot bot) {
				throw new InvalidOperationException(nameof(state));
			}

			await Refresh(bot).ConfigureAwait(false);
		}

		private static async void OnLicenseList(Bot bot, SteamApps.LicenseListCallback callback) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			if (callback == null) {
				throw new ArgumentNullException(nameof(callback));
			}

			if (Config is not { Enabled: true }) {
				return;
			}

			if (GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			Dictionary<uint, ulong> packageTokens = callback.LicenseList.Where(license => !Config.SecretPackageIDs.Contains(license.PackageID) && ((license.PaymentMethod != EPaymentMethod.AutoGrant) || !Config.SkipAutoGrantPackages)).GroupBy(license => license.PackageID).ToDictionary(group => group.Key, group => group.OrderByDescending(license => license.TimeCreated).First().AccessToken);

			GlobalCache.UpdatePackageTokens(packageTokens);

			await Refresh(bot, packageTokens.Keys).ConfigureAwait(false);
		}

		private static async Task Refresh(Bot bot, IReadOnlyCollection<uint>? packageIDs = null) {
			if (bot == null) {
				throw new ArgumentNullException(nameof(bot));
			}

			if (Config is not { Enabled: true }) {
				return;
			}

			if (GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			if (ASF.GlobalDatabase == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			if (!BotSynchronizations.TryGetValue(bot, out (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer) synchronization)) {
				throw new InvalidOperationException(nameof(synchronization));
			}

			if (!await synchronization.RefreshSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				if (!bot.IsConnectedAndLoggedOn) {
					return;
				}

				packageIDs ??= bot.OwnedPackageIDs.Where(package => !Config.SecretPackageIDs.Contains(package.Key) && ((package.Value.PaymentMethod != EPaymentMethod.AutoGrant) || !Config.SkipAutoGrantPackages)).Select(package => package.Key).ToHashSet();

				HashSet<uint> appIDsToRefresh = new();

				foreach (uint packageID in packageIDs.Where(packageID => !Config.SecretPackageIDs.Contains(packageID))) {
					if (!ASF.GlobalDatabase.PackagesDataReadOnly.TryGetValue(packageID, out (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs) packageData) || (packageData.AppIDs == null)) {
						// ASF might not have the package info for us at the moment, we'll retry later
						continue;
					}

					appIDsToRefresh.UnionWith(packageData.AppIDs.Where(appID => !Config.SecretAppIDs.Contains(appID) && GlobalCache.ShouldRefreshAppInfo(appID)));
				}

				if (appIDsToRefresh.Count == 0) {
					bot.ArchiLogger.LogGenericDebug(Strings.BotNoAppsToRefresh);

					return;
				}

				bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingTotalAppAccessTokens, appIDsToRefresh.Count));

				HashSet<uint> appIDsThisRound = new(Math.Min(appIDsToRefresh.Count, SharedInfo.AppInfosPerSingleRequest));

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

						bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingAppAccessTokens, appIDsThisRound.Count));

						SteamApps.PICSTokensCallback response;

						try {
							response = await bot.SteamApps.PICSGetAccessTokens(appIDsThisRound, Enumerable.Empty<uint>()).ToLongRunningTask().ConfigureAwait(false);
						} catch (Exception e) {
							bot.ArchiLogger.LogGenericWarningException(e);

							return;
						}

						bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingAppAccessTokens, appIDsThisRound.Count));

						appIDsThisRound.Clear();

						GlobalCache.UpdateAppTokens(response.AppTokens, response.AppTokensDenied);
					}
				}

				bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingTotalAppAccessTokens, appIDsToRefresh.Count));
				bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingTotalDepots, appIDsToRefresh.Count));

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

						bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingAppInfos, appIDsThisRound.Count));

						AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet response;

						try {
							response = await bot.SteamApps.PICSGetProductInfo(appIDsThisRound.Select(appID => new SteamApps.PICSRequest { ID = appID, AccessToken = GlobalCache.GetAppToken(appID), Public = false }), Enumerable.Empty<SteamApps.PICSRequest>()).ToLongRunningTask().ConfigureAwait(false);
						} catch (Exception e) {
							bot.ArchiLogger.LogGenericWarningException(e);

							return;
						}

						if (response.Results == null) {
							bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, nameof(response.Results)));

							return;
						}

						bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingAppInfos, appIDsThisRound.Count));

						appIDsThisRound.Clear();

						Dictionary<uint, uint> appChangeNumbers = new();

						HashSet<Task<SteamApps.DepotKeyCallback>> depotTasks = new();

						foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo app in response.Results.SelectMany(result => result.Apps.Values)) {
							appChangeNumbers[app.ID] = app.ChangeNumber;

							if (GlobalCache.ShouldRefreshDepotKey(app.ID)) {
								depotTasks.Add(bot.SteamApps.GetDepotDecryptionKey(app.ID, app.ID).ToLongRunningTask());
							}

							foreach (KeyValue depot in app.KeyValues["depots"].Children) {
								if (uint.TryParse(depot.Name, out uint depotID) && !Config.SecretDepotIDs.Contains(depotID) && GlobalCache.ShouldRefreshDepotKey(depotID)) {
									depotTasks.Add(bot.SteamApps.GetDepotDecryptionKey(depotID, app.ID).ToLongRunningTask());
								}
							}
						}

						GlobalCache.UpdateAppChangeNumbers(appChangeNumbers);

						if (depotTasks.Count > 0) {
							bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingDepotKeys, depotTasks.Count));

							IList<SteamApps.DepotKeyCallback> results;

							try {
								results = await Utilities.InParallel(depotTasks).ConfigureAwait(false);
							} catch (Exception e) {
								bot.ArchiLogger.LogGenericWarningException(e);

								return;
							}

							bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingDepotKeys, depotTasks.Count));

							GlobalCache.UpdateDepotKeys(results);
						}
					}
				}

				bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingTotalDepots, appIDsToRefresh.Count));
			} finally {
				TimeSpan timeSpan = TimeSpan.FromHours(SharedInfo.MaximumHoursBetweenRefresh);

				synchronization.RefreshTimer.Change(timeSpan, timeSpan);
				synchronization.RefreshSemaphore.Release();
			}
		}

		private static async void SubmitData(object? state) {
			if (Bot.Bots == null) {
				throw new InvalidOperationException(nameof(Bot.Bots));
			}

			if (Config is not { Enabled: true }) {
				return;
			}

			if (GlobalCache == null) {
				throw new InvalidOperationException(nameof(GlobalCache));
			}

			if (ASF.GlobalConfig == null) {
				throw new InvalidOperationException(nameof(ASF.GlobalConfig));
			}

			if (ASF.WebBrowser == null) {
				throw new InvalidOperationException(nameof(ASF.WebBrowser));
			}

			if (!await SubmissionSemaphore.WaitAsync(0).ConfigureAwait(false)) {
				return;
			}

			try {
				Dictionary<uint, ulong> appTokens = GlobalCache.GetAppTokensForSubmission();
				Dictionary<uint, ulong> packageTokens = GlobalCache.GetPackageTokensForSubmission();
				Dictionary<uint, string> depotKeys = GlobalCache.GetDepotKeysForSubmission();

				if ((appTokens.Count == 0) && (packageTokens.Count == 0) && (depotKeys.Count == 0)) {
					ASF.ArchiLogger.LogGenericInfo(Strings.SubmissionNoNewData);

					return;
				}

				ulong contributorSteamID = (ASF.GlobalConfig.SteamOwnerID > 0) && new SteamID(ASF.GlobalConfig.SteamOwnerID).IsIndividualAccount ? ASF.GlobalConfig.SteamOwnerID : Bot.Bots.Values.Where(bot => bot.SteamID > 0).OrderByDescending(bot => bot.OwnedPackageIDs.Count).FirstOrDefault()?.SteamID ?? 0;

				if (contributorSteamID == 0) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionNoContributorSet, nameof(ASF.GlobalConfig.SteamOwnerID)));

					return;
				}

				Uri request = new(SharedInfo.ServerURL + "/submit");
				RequestData requestData = new(contributorSteamID, appTokens, packageTokens, depotKeys);

				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionInProgress, appTokens.Count, packageTokens.Count, depotKeys.Count));

				ObjectResponse<ResponseData>? response = await ASF.WebBrowser.UrlPostToJsonObject<ResponseData, RequestData>(request, data: requestData, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors).ConfigureAwait(false);

				if (response == null) {
					ASF.ArchiLogger.LogGenericWarning(ArchiSteamFarm.Localization.Strings.WarningFailed);

					return;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, response.StatusCode));

#if NETFRAMEWORK
					if (response.StatusCode == (HttpStatusCode) 429) {
#else
					if (response.StatusCode == HttpStatusCode.TooManyRequests) {
#endif
						TimeSpan startIn = TimeSpan.FromMinutes(Utilities.RandomNext(SharedInfo.MinimumMinutesBeforeFirstUpload, SharedInfo.MaximumMinutesBeforeFirstUpload));

						lock (SubmissionSemaphore) {
							SubmissionTimer.Change(startIn, TimeSpan.FromHours(SharedInfo.MinimumHoursBetweenUploads));
						}

						ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionFailedTooManyRequests, startIn.ToHumanReadable()));
					}

					return;
				}

				if (!response.Content.Success) {
					ASF.ArchiLogger.LogGenericError(ArchiSteamFarm.Localization.Strings.WarningFailed);

					return;
				}

				if (response.Content.Data == null) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.ErrorIsInvalid), nameof(response.Content.Data));

					return;
				}

				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionSuccessful, response.Content.Data.NewApps.Count, response.Content.Data.VerifiedApps.Count, response.Content.Data.NewPackages.Count, response.Content.Data.VerifiedPackages.Count, response.Content.Data.NewDepots.Count, response.Content.Data.VerifiedDepots.Count));

				GlobalCache.UpdateSubmittedData(appTokens, packageTokens, depotKeys);

				if (!response.Content.Data.NewApps.IsEmpty) {
					ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionSuccessfulNewApps, string.Join(", ", response.Content.Data.NewApps)));
				}

				if (!response.Content.Data.VerifiedApps.IsEmpty) {
					ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionSuccessfulVerifiedApps, string.Join(", ", response.Content.Data.VerifiedApps)));
				}

				if (!response.Content.Data.NewPackages.IsEmpty) {
					ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionSuccessfulNewPackages, string.Join(", ", response.Content.Data.NewPackages)));
				}

				if (!response.Content.Data.VerifiedPackages.IsEmpty) {
					ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionSuccessfulVerifiedPackages, string.Join(", ", response.Content.Data.VerifiedPackages)));
				}

				if (!response.Content.Data.NewDepots.IsEmpty) {
					ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionSuccessfulNewDepots, string.Join(", ", response.Content.Data.NewDepots)));
				}

				if (!response.Content.Data.VerifiedDepots.IsEmpty) {
					ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionSuccessfulVerifiedDepots, string.Join(", ", response.Content.Data.VerifiedDepots)));
				}
			} finally {
				SubmissionSemaphore.Release();
			}
		}
	}
}
