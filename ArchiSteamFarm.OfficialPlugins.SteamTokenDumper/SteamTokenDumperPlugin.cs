//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.ComponentModel;
using System.Composition;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.OfficialPlugins.SteamTokenDumper.Data;
using ArchiSteamFarm.OfficialPlugins.SteamTokenDumper.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Interaction;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web;
using ArchiSteamFarm.Web.Responses;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.SteamTokenDumper;

[Export(typeof(IPlugin))]
internal sealed class SteamTokenDumperPlugin : OfficialPlugin, IASF, IBot, IBotCommand2, IBotSteamClient, ISteamPICSChanges {
	private const ushort DepotsRateLimitingDelay = 500;

	[JsonProperty]
	internal static SteamTokenDumperConfig? Config { get; private set; }

	private static readonly ConcurrentDictionary<Bot, IDisposable> BotSubscriptions = new();
	private static readonly ConcurrentDictionary<Bot, (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer)> BotSynchronizations = new();
	private static readonly SemaphoreSlim SubmissionSemaphore = new(1, 1);
	private static readonly Timer SubmissionTimer = new(OnSubmissionTimer);

	private static GlobalCache? GlobalCache;
	private static DateTimeOffset LastUploadAt = DateTimeOffset.MinValue;

	[JsonProperty]
	public override string Name => nameof(SteamTokenDumperPlugin);

	[JsonProperty]
	public override Version Version => typeof(SteamTokenDumperPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task<uint> GetPreferredChangeNumberToStartFrom() => Task.FromResult(GlobalCache?.LastChangeNumber ?? 0);

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

		if (GlobalCache == null) {
			GlobalCache? globalCache = await GlobalCache.Load().ConfigureAwait(false);

			if (globalCache == null) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.FileCouldNotBeLoadedFreshInit, nameof(GlobalCache)));

				GlobalCache = new GlobalCache();
			} else {
				GlobalCache = globalCache;
			}
		}

		if (!isEnabled && (config == null)) {
			ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginDisabledInConfig, nameof(SteamTokenDumperPlugin)));

			return;
		}

		config ??= new SteamTokenDumperConfig();

		if (isEnabled) {
			config.Enabled = true;
		}

		if (!config.Enabled) {
			ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginDisabledInConfig, nameof(SteamTokenDumperPlugin)));
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

		Config = config;

		if (!config.Enabled) {
			return;
		}

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

		switch (args.Length) {
			case 1:
				switch (args[0].ToUpperInvariant()) {
					case "STD":
						return Task.FromResult(ResponseRefreshManually(access, bot));
				}

				break;
			default:
				switch (args[0].ToUpperInvariant()) {
					case "STD":
						return Task.FromResult(ResponseRefreshManually(access, Utilities.GetArgsAsText(args, 1, ","), steamID));
				}

				break;
		}

		return Task.FromResult((string?) null);
	}

	public async Task OnBotDestroy(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (BotSubscriptions.TryRemove(bot, out IDisposable? subscription)) {
			subscription.Dispose();
		}

		if (BotSynchronizations.TryRemove(bot, out (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer) synchronization)) {
			// Ensure the semaphore is empty, otherwise we're risking disposed exceptions
			await synchronization.RefreshSemaphore.WaitAsync().ConfigureAwait(false);

			synchronization.RefreshSemaphore.Dispose();

			await synchronization.RefreshTimer.DisposeAsync().ConfigureAwait(false);
		}
	}

	public async Task OnBotInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (GlobalCache == null) {
			// We can't operate like this anyway, skip initialization of synchronization structures
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
		ArgumentOutOfRangeException.ThrowIfZero(currentChangeNumber);
		ArgumentNullException.ThrowIfNull(appChanges);
		ArgumentNullException.ThrowIfNull(packageChanges);

		GlobalCache?.OnPICSChanges(currentChangeNumber, appChanges);

		return Task.CompletedTask;
	}

	public Task OnPICSChangesRestart(uint currentChangeNumber) {
		ArgumentOutOfRangeException.ThrowIfZero(currentChangeNumber);

		GlobalCache?.OnPICSChangesRestart(currentChangeNumber);

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

		HashSet<uint> packageIDs = callback.LicenseList.Where(static license => !Config.SecretPackageIDs.Contains(license.PackageID) && ((license.PaymentMethod != EPaymentMethod.AutoGrant) || !Config.SkipAutoGrantPackages)).Select(static license => license.PackageID).ToHashSet();

		await Refresh(bot, packageIDs).ConfigureAwait(false);
	}

	private static async void OnSubmissionTimer(object? state = null) => await SubmitData().ConfigureAwait(false);

	private static async Task Refresh(Bot bot, IReadOnlyCollection<uint>? packageIDs = null) {
		ArgumentNullException.ThrowIfNull(bot);

		if (GlobalCache == null) {
			throw new InvalidOperationException(nameof(GlobalCache));
		}

		if (ASF.GlobalDatabase == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalDatabase));
		}

		if (!BotSynchronizations.TryGetValue(bot, out (SemaphoreSlim RefreshSemaphore, Timer RefreshTimer) synchronization)) {
			throw new InvalidOperationException(nameof(synchronization));
		}

		if (!await synchronization.RefreshSemaphore.WaitAsync(0).ConfigureAwait(false)) {
			return;
		}

		SemaphoreSlim depotsRateLimitingSemaphore = new(1, 1);

		try {
			if (!bot.IsConnectedAndLoggedOn) {
				return;
			}

			packageIDs ??= bot.OwnedPackageIDs.Where(static package => (Config?.SecretPackageIDs.Contains(package.Key) != true) && ((package.Value.PaymentMethod != EPaymentMethod.AutoGrant) || (Config?.SkipAutoGrantPackages == false))).Select(static package => package.Key).ToHashSet();

			HashSet<uint> appIDsToRefresh = [];

			foreach (uint packageID in packageIDs.Where(static packageID => Config?.SecretPackageIDs.Contains(packageID) != true)) {
				if (!ASF.GlobalDatabase.PackagesDataReadOnly.TryGetValue(packageID, out PackageData? packageData) || (packageData.AppIDs == null)) {
					// ASF might not have the package info for us at the moment, we'll retry later
					continue;
				}

				appIDsToRefresh.UnionWith(packageData.AppIDs.Where(static appID => (Config?.SecretAppIDs.Contains(appID) != true) && GlobalCache.ShouldRefreshAppInfo(appID)));
			}

			if (appIDsToRefresh.Count == 0) {
				bot.ArchiLogger.LogGenericDebug(Strings.BotNoAppsToRefresh);

				return;
			}

			bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingTotalAppAccessTokens, appIDsToRefresh.Count));

			HashSet<uint> appIDsThisRound = new(Math.Min(appIDsToRefresh.Count, SharedInfo.AppInfosPerSingleRequest));

			using (HashSet<uint>.Enumerator enumerator = appIDsToRefresh.GetEnumerator()) {
				while (true) {
					if (!bot.IsConnectedAndLoggedOn) {
						return;
					}

					while ((appIDsThisRound.Count < SharedInfo.AppInfosPerSingleRequest) && enumerator.MoveNext()) {
						appIDsThisRound.Add(enumerator.Current);
					}

					if (appIDsThisRound.Count == 0) {
						break;
					}

					bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingAppAccessTokens, appIDsThisRound.Count));

					SteamApps.PICSTokensCallback response;

					try {
						response = await bot.SteamApps.PICSGetAccessTokens(appIDsThisRound, Enumerable.Empty<uint>()).ToLongRunningTask().ConfigureAwait(false);
					} catch (Exception e) {
						bot.ArchiLogger.LogGenericWarningException(e);

						appIDsThisRound.Clear();

						continue;
					}

					bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingAppAccessTokens, appIDsThisRound.Count));

					appIDsThisRound.Clear();

					GlobalCache.UpdateAppTokens(response.AppTokens, response.AppTokensDenied);
				}
			}

			bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingTotalAppAccessTokens, appIDsToRefresh.Count));
			bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingTotalDepots, appIDsToRefresh.Count));

			(_, FrozenSet<uint>? knownDepotIDs) = await GlobalCache.KnownDepotIDs.GetValue(ECacheFallback.SuccessPreviously).ConfigureAwait(false);

			using (HashSet<uint>.Enumerator enumerator = appIDsToRefresh.GetEnumerator()) {
				while (true) {
					if (!bot.IsConnectedAndLoggedOn) {
						return;
					}

					while ((appIDsThisRound.Count < SharedInfo.AppInfosPerSingleRequest) && enumerator.MoveNext()) {
						appIDsThisRound.Add(enumerator.Current);
					}

					if (appIDsThisRound.Count == 0) {
						break;
					}

					bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotRetrievingAppInfos, appIDsThisRound.Count));

					AsyncJobMultiple<SteamApps.PICSProductInfoCallback>.ResultSet response;

					try {
						response = await bot.SteamApps.PICSGetProductInfo(appIDsThisRound.Select(static appID => new SteamApps.PICSRequest(appID, GlobalCache.GetAppToken(appID))), Enumerable.Empty<SteamApps.PICSRequest>()).ToLongRunningTask().ConfigureAwait(false);
					} catch (Exception e) {
						bot.ArchiLogger.LogGenericWarningException(e);

						appIDsThisRound.Clear();

						continue;
					}

					if (response.Results == null) {
						bot.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, nameof(response.Results)));

						appIDsThisRound.Clear();

						continue;
					}

					bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingAppInfos, appIDsThisRound.Count));

					appIDsThisRound.Clear();

					Dictionary<uint, uint> appChangeNumbers = new();

					uint depotKeysSuccessful = 0;
					uint depotKeysTotal = 0;

					foreach (SteamApps.PICSProductInfoCallback.PICSProductInfo app in response.Results.SelectMany(static result => result.Apps.Values)) {
						appChangeNumbers[app.ID] = app.ChangeNumber;

						bool shouldFetchMainKey = false;

						foreach (KeyValue depot in app.KeyValues["depots"].Children) {
							if (!uint.TryParse(depot.Name, out uint depotID) || (knownDepotIDs?.Contains(depotID) == true) || (Config?.SecretDepotIDs.Contains(depotID) == true) || !GlobalCache.ShouldRefreshDepotKey(depotID)) {
								continue;
							}

							depotKeysTotal++;

							await depotsRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);

							try {
								SteamApps.DepotKeyCallback depotResponse = await bot.SteamApps.GetDepotDecryptionKey(depotID, app.ID).ToLongRunningTask().ConfigureAwait(false);

								depotKeysSuccessful++;

								if (depotResponse.Result != EResult.OK) {
									continue;
								}

								shouldFetchMainKey = true;

								GlobalCache.UpdateDepotKey(depotResponse);
							} catch (Exception e) {
								// We can still try other depots
								bot.ArchiLogger.LogGenericWarningException(e);
							} finally {
								Utilities.InBackground(
									async () => {
										await Task.Delay(DepotsRateLimitingDelay).ConfigureAwait(false);

										// ReSharper disable once AccessToDisposedClosure - we're waiting for the semaphore to be free before disposing it
										depotsRateLimitingSemaphore.Release();
									}
								);
							}
						}

						// Consider fetching main appID key only if we've actually considered some new depots for resolving
						if (shouldFetchMainKey && (knownDepotIDs?.Contains(app.ID) != true) && GlobalCache.ShouldRefreshDepotKey(app.ID)) {
							depotKeysTotal++;

							await depotsRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);

							try {
								SteamApps.DepotKeyCallback depotResponse = await bot.SteamApps.GetDepotDecryptionKey(app.ID, app.ID).ToLongRunningTask().ConfigureAwait(false);

								depotKeysSuccessful++;

								GlobalCache.UpdateDepotKey(depotResponse);
							} catch (Exception e) {
								// We can still try other depots
								bot.ArchiLogger.LogGenericWarningException(e);
							} finally {
								Utilities.InBackground(
									async () => {
										await Task.Delay(DepotsRateLimitingDelay).ConfigureAwait(false);

										// ReSharper disable once AccessToDisposedClosure - we're waiting for the semaphore to be free before disposing it
										depotsRateLimitingSemaphore.Release();
									}
								);
							}
						}
					}

					bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingDepotKeys, depotKeysSuccessful, depotKeysTotal));

					if (depotKeysSuccessful < depotKeysTotal) {
						// We're not going to record app change numbers, as we didn't fetch all the depot keys we wanted
						continue;
					}

					GlobalCache.UpdateAppChangeNumbers(appChangeNumbers);
				}
			}

			bot.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.BotFinishedRetrievingTotalDepots, appIDsToRefresh.Count));
		} finally {
			if (Config?.Enabled == true) {
				TimeSpan timeSpan = TimeSpan.FromHours(SharedInfo.MaximumHoursBetweenRefresh);

				synchronization.RefreshTimer.Change(timeSpan, timeSpan);
			}

			await depotsRateLimitingSemaphore.WaitAsync().ConfigureAwait(false);

			synchronization.RefreshSemaphore.Release();

			depotsRateLimitingSemaphore.Dispose();
		}
	}

	private static string? ResponseRefreshManually(EAccess access, Bot bot) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentNullException.ThrowIfNull(bot);

		if (access < EAccess.Master) {
			return access > EAccess.None ? bot.Commands.FormatBotResponse(ArchiSteamFarm.Localization.Strings.ErrorAccessDenied) : null;
		}

		if (GlobalCache == null) {
			return bot.Commands.FormatBotResponse(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, nameof(GlobalCache)));
		}

		Utilities.InBackground(
			async () => {
				await Refresh(bot).ConfigureAwait(false);
				await SubmitData().ConfigureAwait(false);
			}
		);

		return bot.Commands.FormatBotResponse(ArchiSteamFarm.Localization.Strings.Done);
	}

	private static string? ResponseRefreshManually(EAccess access, string botNames, ulong steamID = 0) {
		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(botNames);

		if ((steamID != 0) && !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		HashSet<Bot>? bots = Bot.GetBots(botNames);

		if ((bots == null) || (bots.Count == 0)) {
			return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
		}

		if (bots.RemoveWhere(bot => Commands.GetProxyAccess(bot, access, steamID) < EAccess.Master) > 0) {
			if (bots.Count == 0) {
				return access >= EAccess.Owner ? Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.BotNotFound, botNames)) : null;
			}
		}

		if (GlobalCache == null) {
			return Commands.FormatStaticResponse(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, nameof(GlobalCache)));
		}

		Utilities.InBackground(
			async () => {
				await Utilities.InParallel(bots.Select(static bot => Refresh(bot))).ConfigureAwait(false);

				await SubmitData().ConfigureAwait(false);
			}
		);

		return Commands.FormatStaticResponse(ArchiSteamFarm.Localization.Strings.Done);
	}

	private static async Task SubmitData(CancellationToken cancellationToken = default) {
		if (Bot.Bots == null) {
			throw new InvalidOperationException(nameof(Bot.Bots));
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

		if (!await SubmissionSemaphore.WaitAsync(0, cancellationToken).ConfigureAwait(false)) {
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
			SubmitRequest data = new(contributorSteamID, appTokens, packageTokens, depotKeys);

			ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionInProgress, appTokens.Count, packageTokens.Count, depotKeys.Count));

			ObjectResponse<SubmitResponse>? response = await ASF.WebBrowser.UrlPostToJsonObject<SubmitResponse, SubmitRequest>(request, data: data, requestOptions: WebBrowser.ERequestOptions.ReturnClientErrors | WebBrowser.ERequestOptions.AllowInvalidBodyOnErrors, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (response == null) {
				ASF.ArchiLogger.LogGenericWarning(ArchiSteamFarm.Localization.Strings.WarningFailed);

				return;
			}

			// We've communicated with the server and didn't timeout, regardless of the success, this was the last upload attempt
			LastUploadAt = DateTimeOffset.UtcNow;

			if (response.StatusCode.IsClientErrorCode()) {
				ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, ArchiSteamFarm.Localization.Strings.WarningFailedWithError, response.StatusCode));

				switch (response.StatusCode) {
					case HttpStatusCode.Forbidden when Config?.Enabled == true:
						// SteamDB told us to stop submitting data for now
						// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
						lock (SubmissionSemaphore) {
							SubmissionTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
						}

						break;
					case HttpStatusCode.Conflict:
						// SteamDB told us to reset our cache
						GlobalCache.Reset(true);

						break;
					case HttpStatusCode.TooManyRequests when Config?.Enabled == true:
						// SteamDB told us to try again later
#pragma warning disable CA5394 // This call isn't used in a security-sensitive manner
						TimeSpan startIn = TimeSpan.FromMinutes(Random.Shared.Next(SharedInfo.MinimumMinutesBeforeFirstUpload, SharedInfo.MaximumMinutesBeforeFirstUpload));
#pragma warning restore CA5394 // This call isn't used in a security-sensitive manner

						// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
						lock (SubmissionSemaphore) {
							SubmissionTimer.Change(startIn, TimeSpan.FromHours(SharedInfo.HoursBetweenUploads));
						}

						ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.SubmissionFailedTooManyRequests, startIn.ToHumanReadable()));

						break;
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
