// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Composition;
using System.Composition.Convention;
using System.Composition.Hosting;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Integration.Callbacks;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Plugins;

public static class PluginsCore {
	internal static bool HasCustomPluginsLoaded => ActivePlugins.Any(static plugin => plugin is not OfficialPlugin officialPlugin || !officialPlugin.HasSameVersion());

	[ImportMany]
	internal static FrozenSet<IPlugin> ActivePlugins { get; private set; } = [];

	private static FrozenSet<IPluginUpdates> ActivePluginUpdates = [];

	[PublicAPI]
	public static async Task<ICrossProcessSemaphore> GetCrossProcessSemaphore(string objectName) {
		ArgumentException.ThrowIfNullOrEmpty(objectName);

		if (ASF.GlobalConfig == null) {
			throw new InvalidOperationException(nameof(ASF.GlobalConfig));
		}

		// The only purpose of using hashing here is to cut on a potential size of the resource name - paths can be really long, and we almost certainly have some upper limit on the resource name we can allocate
		// At the same time it'd be the best if we avoided all special characters, such as '/' found e.g. in base64, as we can't be sure that it's not a prohibited character in regards to native OS implementation
		// Because of that, SHA256 is sufficient for our case, as it generates alphanumeric characters only, and is barely 256-bit long. We don't need any kind of complex cryptography or collision detection here, any hashing will do, and the shorter the better
		if (!string.IsNullOrEmpty(Program.NetworkGroup)) {
			objectName += $"-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Program.NetworkGroup)))}";
		} else if (!string.IsNullOrEmpty(ASF.GlobalConfig.WebProxyText)) {
			objectName += $"-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ASF.GlobalConfig.WebProxyText)))}";
		}

		string resourceName = OS.GetOsResourceName(objectName);

		if (ActivePlugins.Count == 0) {
			return new CrossProcessFileBasedSemaphore(resourceName);
		}

		IList<ICrossProcessSemaphore?> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<ICrossProcessSemaphoreProvider>().Select(plugin => plugin.GetCrossProcessSemaphore(resourceName))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return new CrossProcessFileBasedSemaphore(resourceName);
		}

		return responses.FirstOrDefault(static response => response != null) ?? new CrossProcessFileBasedSemaphore(resourceName);
	}

	internal static async Task<StringComparer> GetBotsComparer() {
		if (ActivePlugins.Count == 0) {
			return StringComparer.Ordinal;
		}

		IList<StringComparer> results;

		try {
			results = await Utilities.InParallel(ActivePlugins.OfType<IBotsComparer>().Select(static plugin => Task.Run(() => plugin.BotsComparer))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return StringComparer.Ordinal;
		}

		StringComparer? result = results.FirstOrDefault();

		return result ?? StringComparer.Ordinal;
	}

	internal static async Task<uint> GetChangeNumberToStartFrom() {
		uint lastChangeNumber = ASF.GlobalDatabase?.LastChangeNumber ?? 0;

		if ((lastChangeNumber == 0) || (ActivePlugins.Count == 0)) {
			return lastChangeNumber;
		}

		IList<uint> results;

		try {
			results = await Utilities.InParallel(ActivePlugins.OfType<ISteamPICSChanges>().Select(static plugin => plugin.GetPreferredChangeNumberToStartFrom())).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return lastChangeNumber;
		}

		foreach (uint result in results.Where(result => (result > 0) && (result < lastChangeNumber))) {
			lastChangeNumber = result;
		}

		return lastChangeNumber;
	}

	internal static async Task<IMachineInfoProvider?> GetCustomMachineInfoProvider(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return null;
		}

		IList<IMachineInfoProvider?> results;

		try {
			results = await Utilities.InParallel(ActivePlugins.OfType<IBotCustomMachineInfoProvider>().Select(plugin => plugin.GetMachineInfoProvider(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return results.FirstOrDefault(static result => result != null);
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	internal static HashSet<IPluginUpdates> GetPluginsForUpdate(IReadOnlyCollection<string> pluginAssemblyNames) {
		if ((pluginAssemblyNames == null) || (pluginAssemblyNames.Count == 0)) {
			throw new ArgumentNullException(nameof(pluginAssemblyNames));
		}

		// We use ActivePlugins here, since we want to pick up also plugins removed from automatic updates
		return ActivePlugins.OfType<IPluginUpdates>().Where(plugin => pluginAssemblyNames.Contains(plugin.GetType().Assembly.GetName().Name)).ToHashSet();
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	internal static async Task<bool> InitPlugins() {
		if (ActivePlugins.Count > 0) {
			throw new InvalidOperationException(nameof(ActivePlugins));
		}

		HashSet<Assembly>? assemblies = LoadAssemblies();

		if ((assemblies == null) || (assemblies.Count == 0)) {
			ASF.ArchiLogger.LogGenericTrace(Strings.NothingFound);

			return true;
		}

		ASF.ArchiLogger.LogGenericInfo(Strings.FormatInitializing(nameof(Plugins)));

		foreach (Assembly assembly in assemblies) {
			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(Strings.FormatInitializing(assembly.FullName));
			}

			try {
				// This call is bare minimum to verify if the assembly can load itself
				assembly.GetTypes();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericError(Strings.FormatWarningFailedWithError(assembly.FullName));
				ASF.ArchiLogger.LogGenericException(e);

				await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

				return false;
			}
		}

		ConventionBuilder conventions = new();
		conventions.ForTypesDerivedFrom<IPlugin>().Export<IPlugin>();

		ContainerConfiguration configuration = new ContainerConfiguration().WithAssemblies(assemblies, conventions);

		HashSet<IPlugin> activePlugins;

		try {
			using CompositionHost container = configuration.CreateContainer();

			activePlugins = container.GetExports<IPlugin>().ToHashSet();
		} catch (TypeLoadException e) {
			ASF.ArchiLogger.LogGenericError(Strings.FormatWarningFailedWithError(e.TypeName));
			ASF.ArchiLogger.LogGenericException(e);

			await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

			return false;
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

			return false;
		}

		if (activePlugins.Count == 0) {
			return true;
		}

		HashSet<IPlugin> invalidPlugins = [];

		foreach (IPlugin plugin in activePlugins) {
			try {
				ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginLoading(plugin.Name, plugin.Version));

				if (!Program.IgnoreUnsupportedEnvironment && plugin is OfficialPlugin officialPlugin && !officialPlugin.HasSameVersion()) {
					ASF.ArchiLogger.LogGenericError(Strings.FormatWarningUnsupportedOfficialPlugins(plugin.Name, plugin.Version, SharedInfo.Version));

					await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

					return false;
				}

				await plugin.OnLoaded().ConfigureAwait(false);

				ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginLoaded(plugin.Name));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				invalidPlugins.Add(plugin);
			}
		}

		if (invalidPlugins.Count > 0) {
			await Task.Delay(SharedInfo.InformationDelay).ConfigureAwait(false);

			activePlugins.ExceptWith(invalidPlugins);
		}

		if (activePlugins.Count == 0) {
			return true;
		}

		ActivePlugins = activePlugins.ToFrozenSet();

		if (HasCustomPluginsLoaded) {
			ASF.ArchiLogger.LogGenericInfo(Strings.PluginsWarning);

			// Loading plugins changes the program identifier, refresh the console title
			Console.Title = SharedInfo.ProgramIdentifier;
		}

		GlobalConfig.EPluginsUpdateMode pluginsUpdateMode = ASF.GlobalConfig?.PluginsUpdateMode ?? GlobalConfig.DefaultPluginsUpdateMode;
		ImmutableHashSet<string> pluginsUpdateList = ASF.GlobalConfig?.PluginsUpdateList ?? GlobalConfig.DefaultPluginsUpdateList;

		HashSet<IPluginUpdates> activePluginUpdates = [];

		foreach (IPluginUpdates plugin in activePlugins.OfType<IPluginUpdates>()) {
			string? pluginAssemblyName = plugin.GetType().Assembly.GetName().Name;

			if (string.IsNullOrEmpty(pluginAssemblyName)) {
				ASF.ArchiLogger.LogNullError(nameof(pluginAssemblyName));

				continue;
			}

			switch (pluginsUpdateMode) {
				case GlobalConfig.EPluginsUpdateMode.Blacklist when !pluginsUpdateList.Contains(pluginAssemblyName):
				case GlobalConfig.EPluginsUpdateMode.Whitelist when pluginsUpdateList.Contains(pluginAssemblyName):
					activePluginUpdates.Add(plugin);

					ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginUpdateEnabled(plugin.Name, pluginAssemblyName));

					break;
				case GlobalConfig.EPluginsUpdateMode.Blacklist when pluginsUpdateList.Contains(pluginAssemblyName):
				case GlobalConfig.EPluginsUpdateMode.Whitelist when !pluginsUpdateList.Contains(pluginAssemblyName):
					ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginUpdateDisabled(plugin.Name, pluginAssemblyName));

					break;
			}
		}

		if (activePluginUpdates.Count > 0) {
			if (activePluginUpdates.Any(static plugin => plugin is not OfficialPlugin)) {
				ASF.ArchiLogger.LogGenericWarning(Strings.CustomPluginUpdatesEnabled);
			}

			ActivePluginUpdates = activePluginUpdates.ToFrozenSet();
		}

		return true;
	}

	internal static HashSet<Assembly>? LoadAssemblies() {
		HashSet<Assembly>? assemblies = null;

		string pluginsPath = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.PluginsDirectory);

		if (Directory.Exists(pluginsPath)) {
			HashSet<Assembly>? loadedAssemblies = LoadAssembliesFrom(pluginsPath);

			if (loadedAssemblies?.Count > 0) {
				assemblies = loadedAssemblies;
			}
		}

		string customPluginsPath = Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.PluginsDirectory);

		if ((pluginsPath != customPluginsPath) && Directory.Exists(customPluginsPath)) {
			HashSet<Assembly>? loadedAssemblies = LoadAssembliesFrom(customPluginsPath);

			if (loadedAssemblies?.Count > 0) {
				if (assemblies?.Count > 0) {
					assemblies.UnionWith(loadedAssemblies);
				} else {
					assemblies = loadedAssemblies;
				}
			}
		}

		return assemblies;
	}

	internal static async Task OnASFInitModules(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IASF>().Select(plugin => plugin.OnASFInit(additionalConfigProperties))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0) {
		ArgumentNullException.ThrowIfNull(bot);

		if (!Enum.IsDefined(access)) {
			throw new InvalidEnumArgumentException(nameof(access), (int) access, typeof(EAccess));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		if ((args == null) || (args.Length == 0)) {
			throw new ArgumentNullException(nameof(args));
		}

		if (ActivePlugins.Count == 0) {
			return null;
		}

		IList<string?> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<IBotCommand2>().Select(plugin => plugin.OnBotCommand(bot, access, message, args, steamID))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return string.Join(Environment.NewLine, responses.Where(static response => !string.IsNullOrEmpty(response)));
	}

	internal static async Task OnBotDestroy(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBot>().Select(plugin => plugin.OnBotDestroy(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotDisconnected(Bot bot, EResult reason) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotConnection>().Select(plugin => plugin.OnBotDisconnected(bot, reason))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotFarmingFinished(Bot bot, bool farmedSomething) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotCardsFarmerInfo>().Select(plugin => plugin.OnBotFarmingFinished(bot, farmedSomething))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotFarmingStarted(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotCardsFarmerInfo>().Select(plugin => plugin.OnBotFarmingStarted(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotFarmingStopped(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotCardsFarmerInfo>().Select(plugin => plugin.OnBotFarmingStopped(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task<bool> OnBotFriendRequest(Bot bot, ulong steamID) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((steamID == 0) || !new SteamID(steamID).IsValid) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		if (ActivePlugins.Count == 0) {
			return false;
		}

		IList<bool> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<IBotFriendRequest>().Select(plugin => plugin.OnBotFriendRequest(bot, steamID))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return false;
		}

		return responses.Any(static response => response);
	}

	internal static async Task OnBotInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBot>().Select(plugin => plugin.OnBotInit(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotModules>().Select(plugin => plugin.OnBotInitModules(bot, additionalConfigProperties))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotLoggedOn(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotConnection>().Select(plugin => plugin.OnBotLoggedOn(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task<string?> OnBotMessage(Bot bot, ulong steamID, string message) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((steamID == 0) || !new SteamID(steamID).IsIndividualAccount) {
			throw new ArgumentOutOfRangeException(nameof(steamID));
		}

		ArgumentException.ThrowIfNullOrEmpty(message);

		if (ActivePlugins.Count == 0) {
			return null;
		}

		IList<string?> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<IBotMessage>().Select(plugin => plugin.OnBotMessage(bot, steamID, message))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return string.Join(Environment.NewLine, responses.Where(static response => !string.IsNullOrEmpty(response)));
	}

	internal static async Task OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(callbackManager);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotSteamClient>().Select(plugin => plugin.OnBotSteamCallbacksInit(bot, callbackManager))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task<HashSet<ClientMsgHandler>?> OnBotSteamHandlersInit(Bot bot) {
		ArgumentNullException.ThrowIfNull(bot);

		if (ActivePlugins.Count == 0) {
			return null;
		}

		IList<IReadOnlyCollection<ClientMsgHandler>?> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<IBotSteamClient>().Select(plugin => plugin.OnBotSteamHandlersInit(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return responses.Where(static response => response != null).SelectMany(static handlers => handlers ?? []).ToHashSet();
	}

	internal static async Task<bool> OnBotTradeOffer(Bot bot, TradeOffer tradeOffer, ParseTradeResult.EResult asfResult) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(tradeOffer);

		if (ActivePlugins.Count == 0) {
			return false;
		}

		IList<bool> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<IBotTradeOffer2>().Select(plugin => plugin.OnBotTradeOffer(bot, tradeOffer, asfResult))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return false;
		}

		return responses.Any(static response => response);
	}

	internal static async Task OnBotTradeOfferResults(Bot bot, IReadOnlyCollection<ParseTradeResult> tradeResults) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((tradeResults == null) || (tradeResults.Count == 0)) {
			throw new ArgumentNullException(nameof(tradeResults));
		}

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotTradeOfferResults>().Select(plugin => plugin.OnBotTradeOfferResults(bot, tradeResults))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotUserNotifications(Bot bot, IReadOnlyCollection<UserNotificationsCallback.EUserNotification> newNotifications) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((newNotifications == null) || (newNotifications.Count == 0)) {
			throw new ArgumentNullException(nameof(newNotifications));
		}

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotUserNotifications>().Select(plugin => plugin.OnBotUserNotifications(bot, newNotifications))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
		ArgumentOutOfRangeException.ThrowIfZero(currentChangeNumber);
		ArgumentNullException.ThrowIfNull(appChanges);
		ArgumentNullException.ThrowIfNull(packageChanges);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<ISteamPICSChanges>().Select(plugin => plugin.OnPICSChanges(currentChangeNumber, appChanges, packageChanges))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnPICSChangesRestart(uint currentChangeNumber) {
		ArgumentOutOfRangeException.ThrowIfZero(currentChangeNumber);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<ISteamPICSChanges>().Select(plugin => plugin.OnPICSChangesRestart(currentChangeNumber))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnSelfPersonaState(Bot bot, SteamFriends.PersonaStateCallback data, string? nickname, string? avatarHash) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(data);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotIdentity>().Select(plugin => plugin.OnSelfPersonaState(bot, data, nickname, avatarHash))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnUpdateFinished(Version newVersion) {
		ArgumentNullException.ThrowIfNull(newVersion);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IUpdateAware>().Select(plugin => plugin.OnUpdateFinished(SharedInfo.Version, newVersion))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnUpdateProceeding(Version newVersion) {
		ArgumentNullException.ThrowIfNull(newVersion);

		if (ActivePlugins.Count == 0) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IUpdateAware>().Select(plugin => plugin.OnUpdateProceeding(SharedInfo.Version, newVersion))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task<bool> UpdatePlugins(Version asfVersion, bool asfUpdate, GlobalConfig.EUpdateChannel? updateChannel = null, bool updateOverride = false, bool forced = false) {
		ArgumentNullException.ThrowIfNull(asfVersion);

		if (updateChannel.HasValue && !Enum.IsDefined(updateChannel.Value)) {
			throw new InvalidEnumArgumentException(nameof(updateChannel), (int) updateChannel, typeof(GlobalConfig.EUpdateChannel));
		}

		if (ActivePluginUpdates.Count == 0) {
			return false;
		}

		return await UpdatePlugins(asfVersion, asfUpdate, ActivePluginUpdates, updateChannel, updateOverride, forced).ConfigureAwait(false);
	}

	internal static async Task<bool> UpdatePlugins(Version asfVersion, bool asfUpdate, IReadOnlyCollection<IPluginUpdates> plugins, GlobalConfig.EUpdateChannel? updateChannel = null, bool updateOverride = false, bool forced = false) {
		ArgumentNullException.ThrowIfNull(asfVersion);

		if ((plugins == null) || (plugins.Count == 0)) {
			throw new ArgumentNullException(nameof(plugins));
		}

		if (updateChannel.HasValue && !Enum.IsDefined(updateChannel.Value)) {
			throw new InvalidEnumArgumentException(nameof(updateChannel), (int) updateChannel, typeof(GlobalConfig.EUpdateChannel));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		updateChannel ??= ASF.GlobalConfig?.UpdateChannel ?? GlobalConfig.DefaultUpdateChannel;

		if (updateChannel == GlobalConfig.EUpdateChannel.None) {
			return false;
		}

		ASF.ArchiLogger.LogGenericInfo(Strings.PluginUpdatesChecking);

		IList<bool> pluginUpdates = await Utilities.InParallel(plugins.Select(plugin => UpdatePlugin(asfVersion, asfUpdate, plugin, updateChannel.Value, updateOverride, forced))).ConfigureAwait(false);

		return pluginUpdates.Any(static updated => updated);
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private static HashSet<Assembly>? LoadAssembliesFrom(string path) {
		ArgumentException.ThrowIfNullOrEmpty(path);

		if (!Directory.Exists(path)) {
			return null;
		}

		HashSet<Assembly> assemblies = [];

		try {
			foreach (string assemblyPath in Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories)) {
				string? assemblyDirectory = Path.GetDirectoryName(assemblyPath);

				if (string.IsNullOrEmpty(assemblyDirectory)) {
					throw new InvalidOperationException(nameof(assemblyDirectory));
				}

				// Skip from loading those files that come from update directories
				// We determine that by checking if any directory name along the path to the assembly matches
				bool skip = false;

				string? relativeAssemblyDirectory = Path.GetRelativePath(path, assemblyDirectory);
				string? relativeAssemblyDirectoryName = Path.GetFileName(relativeAssemblyDirectory);

				while (!string.IsNullOrEmpty(relativeAssemblyDirectoryName)) {
					if (relativeAssemblyDirectoryName is SharedInfo.UpdateDirectoryOld or SharedInfo.UpdateDirectoryNew) {
						skip = true;

						break;
					}

					relativeAssemblyDirectory = Path.GetDirectoryName(relativeAssemblyDirectory);
					relativeAssemblyDirectoryName = Path.GetFileName(relativeAssemblyDirectory);
				}

				if (skip) {
					ASF.ArchiLogger.LogGenericTrace(Strings.FormatWarningSkipping(assemblyPath));

					continue;
				}

				Assembly assembly;

				try {
					assembly = Assembly.LoadFrom(assemblyPath);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericError(Strings.FormatErrorIsInvalid(assemblyPath));
					ASF.ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				assemblies.Add(assembly);
			}
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return assemblies;
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL3000", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private static async Task<bool> UpdatePlugin(Version asfVersion, bool asfUpdate, IPluginUpdates plugin, GlobalConfig.EUpdateChannel updateChannel, bool updateOverride, bool forced) {
		ArgumentNullException.ThrowIfNull(asfVersion);
		ArgumentNullException.ThrowIfNull(plugin);

		if (!Enum.IsDefined(updateChannel) || (updateChannel == GlobalConfig.EUpdateChannel.None)) {
			throw new InvalidEnumArgumentException(nameof(updateChannel), (int) updateChannel, typeof(GlobalConfig.EUpdateChannel));
		}

		if (ASF.WebBrowser == null) {
			throw new InvalidOperationException(nameof(ASF.WebBrowser));
		}

		string pluginName;

		try {
			pluginName = plugin.Name;

			ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginUpdateChecking(pluginName));

			string? assemblyDirectory = Path.GetDirectoryName(plugin.GetType().Assembly.Location);

			if (string.IsNullOrEmpty(assemblyDirectory)) {
				throw new InvalidOperationException(nameof(assemblyDirectory));
			}

			// If directories from previous update exist, it's a good idea to purge them now
			if (!await Utilities.UpdateCleanup(assemblyDirectory).ConfigureAwait(false)) {
				return false;
			}

			Uri? releaseURL = await plugin.GetTargetReleaseURL(asfVersion, BuildInfo.Variant, asfUpdate, updateChannel, forced).ConfigureAwait(false);

			if (releaseURL == null) {
				return false;
			}

			if (!updateOverride && ((ASF.GlobalConfig?.UpdatePeriod ?? GlobalConfig.DefaultUpdatePeriod) == 0)) {
				ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginUpdateNewVersionAvailable(pluginName));

				return false;
			}

			ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginUpdateInProgress(pluginName));

			Progress<byte> progressReporter = new();

			progressReporter.ProgressChanged += onProgressChanged;

			BinaryResponse? response;

			try {
				response = await ASF.WebBrowser.UrlGetToBinary(releaseURL, progressReporter: progressReporter).ConfigureAwait(false);
			} finally {
				progressReporter.ProgressChanged -= onProgressChanged;
			}

			if (response?.Content == null) {
				return false;
			}

			ASF.ArchiLogger.LogGenericInfo(Strings.PatchingFiles);

			byte[] responseBytes = response.Content as byte[] ?? response.Content.ToArray();

			MemoryStream memoryStream = new(responseBytes);

			await using (memoryStream.ConfigureAwait(false)) {
				ZipArchive zipArchive = new(memoryStream);

				await using (zipArchive.ConfigureAwait(false)) {
					await plugin.OnPluginUpdateProceeding().ConfigureAwait(false);

					if (!await Utilities.UpdateFromArchive(zipArchive, assemblyDirectory).ConfigureAwait(false)) {
						ASF.ArchiLogger.LogGenericError(Strings.WarningFailed);

						return false;
					}
				}
			}
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return false;
		}

		ASF.ArchiLogger.LogGenericInfo(Strings.FormatPluginUpdateFinished(pluginName));

		try {
			await plugin.OnPluginUpdateFinished().ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}

		return true;

		void onProgressChanged(object? sender, byte progressPercentage) {
			ArgumentOutOfRangeException.ThrowIfGreaterThan(progressPercentage, 100);

			Utilities.OnProgressChanged(pluginName, progressPercentage);
		}
	}
}
