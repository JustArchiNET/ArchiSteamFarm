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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
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

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper;

[Export(typeof(IPlugin))]
internal sealed class SteamTokenDumperPlugin : OfficialPlugin, IASF, IBot, IBotCommand2, IBotSteamClient, ISteamPICSChanges {
	[JsonProperty]
	internal static SteamTokenDumperConfig? Config { get; private set; }

	private static readonly ConcurrentDictionary<Bot, IDisposable> BotSubscriptions = new();
	private static readonly ConcurrentDictionary<Bot, (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer)> BotSynchronizations = new();
	private static readonly SemaphoreSlim SubmissionSemaphore = new(1, 1);
	private static readonly Timer SubmissionTimer = new(SubmitData);

	private static GlobalCache? GlobalCache;
	private static DateTimeOffset LastUploadAt = DateTimeOffset.MinValue;

	[JsonProperty]
	public override string Name => nameof(SteamTokenDumperPlugin);

	[JsonProperty]
	public override Version Version => typeof(SteamTokenDumperPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task<uint> GetPreferredChangeNumberToStartFrom() => Task.FromResult(Config?.Enabled == true ? GlobalCache?.LastChangeNumber ?? 0 : 0);

	public async Task OnASFInit(IReadOnlyDictionary<string, JToken>? additionalConfigProperties = null) {
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

		Config = config;

#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
		TimeSpan startIn = TimeSpan.FromMinutes(Random.Shared.Next(SharedInfo.MinimumMinutesBeforeFirstUpload, SharedInfo.MaximumMinutesBeforeFirstUpload));
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner

		// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
		lock (SubmissionSemaphore) {
			SubmissionTimer.Change(startIn, TimeSpan.FromHours(SharedInfo.HoursBetweenUploads));
		}

		ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginInitializedAndEnabled, nameof(SteamTokenDumperPlugin), startIn.ToHumanReadable()));
	}

	public Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		if ((args == null) || (args.Length == 0)) {
			throw new ArgumentNullException(nameof(args));
		}

		switch (args[0].ToUpperInvariant()) {
			case "STD" when access >= EAccess.Owner:
				if (Config is not { Enabled: true }) {
					return Task.FromResult((string?) string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, nameof(Config)));
				}

				TimeSpan minimumTimeBetweenUpload = TimeSpan.FromMinutes(SharedInfo.MinimumMinutesBetweenUploads);

				if (LastUploadAt + minimumTimeBetweenUpload > DateTimeOffset.UtcNow) {
					return Task.FromResult((string?) string.Format(CultureInfo.CurrentCulture, Strings.SubmissionFailedTooManyRequests, minimumTimeBetweenUpload.ToHumanReadable()));
				}

				// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
				lock (SubmissionSemaphore) {
					SubmissionTimer.Change(TimeSpan.Zero, TimeSpan.FromHours(SharedInfo.HoursBetweenUploads));
				}

				return Task.FromResult((string?) ArchiSteamFarm.Localization.Strings.Done);
			case "STD" when access > EAccess.None:
				return Task.FromResult((string?) ArchiSteamFarm.Localization.Strings.ErrorAccessDenied);
			default:
				return Task.FromResult((string?) null);
		}
	}

	public async Task OnBotDestroy(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (BotSubscriptions.TryRemove(bot, out IDisposable? subscription)) {
			subscription.Dispose();
		}

		if (BotSynchronizations.TryRemove(bot, out (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer) synchronization)) {
			synchronization.RefreshSemaphore.Dispose();

			await synchronization.RefreshTimer.DisposeAsync().ConfigureAwait(false);
		}
	}

	public async Task OnBotInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

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

	public Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(callbackManager);

		if (BotSubscriptions.TryRemove(bot, out IDisposable? subscription)) {
			subscription.Dispose();
		}

		if (Config is not { Enabled: true }) {
			return Task.CompletedTask;
		}

		subscription = callbackManager.Subscribe<SteamApps.LicenseListCallback>(callback => OnLicenseList(bot, callback));

		if (!BotSubscriptions.TryAdd(bot, subscription)) {
			subscription.Dispose();
		}

		return Task.CompletedTask;
	}

	public Task<IReadOnlyCollection<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) => Task.FromResult((IReadOnlyCollection<ClientMsgHandler>?) null);

	public override Task OnLoaded() {
		Utilities.WarnAboutIncompleteTranslation(Strings.ResourceManager);

		return Task.CompletedTask;
	}

	public Task OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
		if (currentChangeNumber == 0) {
			throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
		}

		ArgumentNullException.ThrowIfNull(appChanges);
		ArgumentNullException.ThrowIfNull(packageChanges);

		if (Config is not { Enabled: true }) {
			return Task.CompletedTask;
		}

		if (GlobalCache == null) {
			throw new InvalidOperationException(nameof(GlobalCache));
		}

		GlobalCache.OnPICSChanges(currentChangeNumber, appChanges);

		return Task.CompletedTask;
	}

	public Task OnPICSChangesRestart(uint currentChangeNumber) {
		if (currentChangeNumber == 0) {
			throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
		}

		if (Config is not { Enabled: true }) {
			return Task.CompletedTask;
		}

		if (GlobalCache == null) {
			throw new InvalidOperationException(nameof(GlobalCache));
		}

		GlobalCache.OnPICSChangesRestart(currentChangeNumber);

		return Task.CompletedTask;
	}

	private static async void OnBotRefreshTimer(object? state) {
		if (state is not Bot bot) {
			throw new InvalidOperationException(nameof(state));
		}

		await Refresh(bot).ConfigureAwait(false);
	}

	private static async void OnLicenseList(Bot bot, SteamApps.LicenseListCallback callback) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(callback);

		if (Config is not { Enabled: true }) {
			return;
		}

		if (GlobalCache == null) {
			throw new InvalidOperationException(nameof(GlobalCache));
		}

		Dictionary<uint, ulong> packageTokens = callback.LicenseList.Where(static license => !Config.SecretPackageIDs.Contains(license.PackageID) && ((license.PaymentMethod != EPaymentMethod.AutoGrant) || !Config.SkipAutoGrantPackages)).GroupBy(static license => license.PackageID).ToDictionary(static group => group.Key, static group => group.OrderByDescending(static license => license.TimeCreated).First().AccessToken);

		GlobalCache.UpdatePackageTokens(packageTokens);

		await Refresh(bot, packageTokens.Keys).ConfigureAwait(false);
	}

	private static async Task Refresh(Bot bot, IReadOnlyCollection<uint>? packageIDs = null) {
		ArgumentNullException.ThrowIfNull(bot);

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

			packageIDs ??= bot.OwnedPackageIDs.Where(static package => !Config.SecretPackageIDs.Contains(package.Key) && ((package.Value.PaymentMethod != EPaymentMethod.AutoGrant) || !Config.SkipAutoGrantPackages)).Select(static package => package.Key).ToHashSet();

			HashSet<uint> appIDsToRefresh = new();

			foreach (uint packageID in packageIDs.Where(static packageID => !Config.SecretPackageIDs.Contains(packageID))) {
				if (!ASF.GlobalDatabase.PackagesDataReadOnly.TryGetValue(packageID, out (uint ChangeNumber, ImmutableHashSet<uint>? AppIDs) packageData) || (packageData.AppIDs == null)) {
					// ASF might not have the package info for us at the moment, we'll retry later
					continue;
				}

				appIDsToRefresh.UnionWith(packageData.AppIDs.Where(static appID => !Config.SecretAppIDs.Contains(appID) && GlobalCache.ShouldRefreshAppInfo(appID)));
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
						response = await bot.SteamApps.PICSGetProductInfo(appIDsThisRound.Select(static appID => new SteamApps.PICSRequest(appID, GlobalCache.GetAppToken(appID))), Enumerable.Empty<SteamApps.PICSRequest>()).ToLongRunningTask().ConfigureAwait(false);
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

					foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo app in response.Results.SelectMany(static result => result.Apps.Values)) {
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

					GlobalCache.UpdateAppChangeNumbers(appChangeNumbers);
				}
			}

			bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingTotalDepots, appIDsToRefresh.Count));
		} finally {
			TimeSpan timeSpan = TimeSpan.FromHours(SharedInfo.MaximumHoursBetweenRefresh);

			synchronization.RefreshTimer.Change(timeSpan, timeSpan);
			synchronization.RefreshSemaphore.Release();
		}
	}

	private static async void SubmitData(object? state = null) {
		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
		}

		if (Config is not { Enabled: true }) {
			return;
		}

		if (GlobalCache == null) {
			throw new InvalidOperationException(nameof(GlobalCache));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		if (LastUploadAt + TimeSpan.FromMinutes(SharedInfo.MinimumMinutesBetweenUploads) > DateTimeOffset.UtcNow) {
			return;
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

			ulong contributorSteamID = ASF.GlobalConfig is { SteamOwnerID: > 0 } && new SteamID(ASF.GlobalConfig.SteamOwnerID).IsIndividualAccount ? ASF.GlobalConfig.SteamOwnerID : Bot.Bots.Values.Where(static bot => bot.SteamID > 0).MaxBy(static bot => bot.OwnedPackageIDs.Count)?.SteamID ?? 0;

			if (contributorSteamID == 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionNoContributorSet, nameof(ASF.GlobalConfig.SteamOwnerID)));

				return;
			}

			Uri request = new($"{SharedInfo.ServerURL}/submit");
			RequestData requestData = new(contributorSteamID, appTokens, packageTokens, depotKeys);

			ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionInProgress, appTokens.Count, packageTokens.Count, depotKeys.Count));

			OptionalObjectResponse<ResponseData>? response = await ASF.WebBrowser.UrlPostToOptionalJsonObject<ResponseData, RequestData>(request, data: requestData, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors).ConfigureAwait(false);

			if (response == null) {
				ASF.ArchiLogger.LogGenericWarning(ArchiSteamFarm.Localization.Strings.WarningFailed);

				return;
			}

			// We've communicated with the server and didn't timeout, regardless of the success, this was the last upload attempt
			LastUploadAt = DateTimeOffset.UtcNow;

			if (response.StatusCode.IsClientErrorCode()) {
				ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, response.StatusCode));

#if NETFRAMEWORK
				if (response.StatusCode == (HttpStatusCode) 429) {
#else
				if (response.StatusCode == HttpStatusCode.TooManyRequests) {
#endif
#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
					TimeSpan startIn = TimeSpan.FromMinutes(Random.Shared.Next(SharedInfo.MinimumMinutesBeforeFirstUpload, SharedInfo.MaximumMinutesBeforeFirstUpload));
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner

					// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
					lock (SubmissionSemaphore) {
						SubmissionTimer.Change(startIn, TimeSpan.FromHours(SharedInfo.HoursBetweenUploads));
					}

					ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionFailedTooManyRequests, startIn.ToHumanReadable()));
				}

				return;
			}

			if (response.Content is not { Success: true }) {
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
