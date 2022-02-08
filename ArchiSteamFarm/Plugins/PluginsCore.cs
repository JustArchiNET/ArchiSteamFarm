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
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel;
using System.Composition;
using System.Composition.Convention;
using System.Composition.Hosting;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Data;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Steam.Integration.Callbacks;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm.Plugins;

internal static class PluginsCore {
	internal static bool HasCustomPluginsLoaded => ActivePlugins?.Any(static plugin => plugin is not OfficialPlugin officialPlugin || !officialPlugin.HasSameVersion()) == true;

	[ImportMany]
	internal static ImmutableHashSet<IPlugin>? ActivePlugins { get; private set; }

	internal static async Task<StringComparer> GetBotsComparer() {
		if (ActivePlugins == null) {
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

		if ((lastChangeNumber == 0) || (ActivePlugins == null)) {
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

	internal static async Task<ICrossProcessSemaphore> GetCrossProcessSemaphore(string objectName) {
		if (string.IsNullOrEmpty(objectName)) {
			throw new ArgumentNullException(nameof(objectName));
		}

		string resourceName = OS.GetOsResourceName(objectName);

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

	internal static async Task<IMachineInfoProvider?> GetCustomMachineInfoProvider() {
		if (ActivePlugins == null) {
			return null;
		}

		IList<IMachineInfoProvider> results;

		try {
			results = await Utilities.InParallel(ActivePlugins.OfType<ICustomMachineInfoProvider>().Select(static plugin => Task.Run(() => plugin.MachineInfoProvider))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return results.FirstOrDefault();
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	internal static bool InitPlugins() {
		if (ActivePlugins != null) {
			return false;
		}

		HashSet<Assembly>? assemblies = LoadAssemblies();

		if ((assemblies == null) || (assemblies.Count == 0)) {
			ASF.ArchiLogger.LogGenericTrace(Strings.NothingFound);

			return true;
		}

		ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.Initializing, nameof(Plugins)));

		foreach (Assembly assembly in assemblies) {
			if (Debugging.IsUserDebugging) {
				ASF.ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Initializing, assembly.FullName));
			}

			try {
				// This call is bare minimum to verify if the assembly can load itself
				assembly.GetTypes();
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError, assembly.FullName));
				ASF.ArchiLogger.LogGenericException(e);

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
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return false;
		}

		if (activePlugins.Count == 0) {
			return true;
		}

		HashSet<IPlugin> invalidPlugins = new();

		foreach (IPlugin plugin in activePlugins) {
			try {
				string pluginName = plugin.Name;

				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginLoading, pluginName, plugin.Version));
				plugin.OnLoaded();
				ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginLoaded, pluginName));
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				invalidPlugins.Add(plugin);
			}
		}

		if (invalidPlugins.Count > 0) {
			activePlugins.ExceptWith(invalidPlugins);

			if (activePlugins.Count == 0) {
				return false;
			}
		}

		ActivePlugins = activePlugins.ToImmutableHashSet();

		if (HasCustomPluginsLoaded) {
			ASF.ArchiLogger.LogGenericInfo(Strings.PluginsWarning);

			// Loading plugins changes the program identifier, refresh the console title
			Console.Title = SharedInfo.ProgramIdentifier;
		}

		return invalidPlugins.Count == 0;
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

	internal static async Task OnASFInitModules(IReadOnlyDictionary<string, JToken>? additionalConfigProperties = null) {
		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if (string.IsNullOrEmpty(message)) {
			throw new ArgumentNullException(nameof(message));
		}

		if ((args == null) || (args.Length == 0)) {
			throw new ArgumentNullException(nameof(args));
		}

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBot>().Select(plugin => plugin.OnBotInit(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JToken>? additionalConfigProperties = null) {
		ArgumentNullException.ThrowIfNull(bot);

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if (string.IsNullOrEmpty(message)) {
			throw new ArgumentNullException(nameof(message));
		}

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return null;
		}

		IList<IReadOnlyCollection<ClientMsgHandler>?> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<IBotSteamClient>().Select(plugin => plugin.OnBotSteamHandlersInit(bot))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}

		return responses.Where(static response => response != null).SelectMany(static handlers => handlers ?? Enumerable.Empty<ClientMsgHandler>()).ToHashSet();
	}

	internal static async Task<bool> OnBotTradeOffer(Bot bot, TradeOffer tradeOffer) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(tradeOffer);

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return false;
		}

		IList<bool> responses;

		try {
			responses = await Utilities.InParallel(ActivePlugins.OfType<IBotTradeOffer>().Select(plugin => plugin.OnBotTradeOffer(bot, tradeOffer))).ConfigureAwait(false);
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
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

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IBotUserNotifications>().Select(plugin => plugin.OnBotUserNotifications(bot, newNotifications))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnPICSChanges(uint currentChangeNumber, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> appChanges, IReadOnlyDictionary<uint, SteamApps.PICSChangesCallback.PICSChangeData> packageChanges) {
		if (currentChangeNumber == 0) {
			throw new ArgumentOutOfRangeException(nameof(currentChangeNumber));
		}

		ArgumentNullException.ThrowIfNull(appChanges);
		ArgumentNullException.ThrowIfNull(packageChanges);

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<ISteamPICSChanges>().Select(plugin => plugin.OnPICSChanges(currentChangeNumber, appChanges, packageChanges))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnPICSChangesRestart(uint currentChangeNumber) {
		if (currentChangeNumber == 0) {
			throw new ArgumentNullException(nameof(currentChangeNumber));
		}

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<ISteamPICSChanges>().Select(plugin => plugin.OnPICSChangesRestart(currentChangeNumber))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnUpdateFinished(Version newVersion) {
		if (newVersion == null) {
			throw new ArgumentNullException(nameof(newVersion));
		}

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IUpdateAware>().Select(plugin => plugin.OnUpdateFinished(SharedInfo.Version, newVersion))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	internal static async Task OnUpdateProceeding(Version newVersion) {
		if (newVersion == null) {
			throw new ArgumentNullException(nameof(newVersion));
		}

		if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
			return;
		}

		try {
			await Utilities.InParallel(ActivePlugins.OfType<IUpdateAware>().Select(plugin => plugin.OnUpdateProceeding(SharedInfo.Version, newVersion))).ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		}
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private static HashSet<Assembly>? LoadAssembliesFrom(string path) {
		if (string.IsNullOrEmpty(path)) {
			throw new ArgumentNullException(nameof(path));
		}

		if (!Directory.Exists(path)) {
			return null;
		}

		HashSet<Assembly> assemblies = new();

		try {
			foreach (string assemblyPath in Directory.EnumerateFiles(path, "*.dll", SearchOption.AllDirectories)) {
				Assembly assembly;

				try {
					assembly = Assembly.LoadFrom(assemblyPath);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, assemblyPath));
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
}
