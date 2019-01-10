//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Composition;
using System.Composition.Convention;
using System.Composition.Hosting;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json.Linq;
using SteamKit2;

namespace ArchiSteamFarm.Plugins {
	internal static class Core {
		[ImportMany]
		private static ImmutableHashSet<IPlugin> ActivePlugins { get; set; }

		internal static bool InitPlugins() {
			string pluginsPath = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.PluginsDirectory);

			if (!Directory.Exists(pluginsPath)) {
				ASF.ArchiLogger.LogGenericTrace(Strings.NothingFound);

				return true;
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.Initializing, nameof(Plugins)));

			HashSet<Assembly> assemblies = new HashSet<Assembly>();

			try {
				foreach (string assemblyPath in Directory.EnumerateFiles(pluginsPath, "*.dll")) {
					Assembly assembly;

					try {
						assembly = Assembly.LoadFrom(assemblyPath);
					} catch (Exception e) {
						ASF.ArchiLogger.LogGenericException(e);

						continue;
					}

					assemblies.Add(assembly);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return false;
			}

			if (assemblies.Count == 0) {
				ASF.ArchiLogger.LogGenericInfo(Strings.NothingFound);

				return true;
			}

			ConventionBuilder conventions = new ConventionBuilder();
			conventions.ForTypesDerivedFrom<IPlugin>().Export<IPlugin>();

			ContainerConfiguration configuration = new ContainerConfiguration().WithAssemblies(assemblies, conventions);

			try {
				using (CompositionHost container = configuration.CreateContainer()) {
					ActivePlugins = container.GetExports<IPlugin>().ToImmutableHashSet();
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return false;
			}

			HashSet<IPlugin> invalidPlugins = new HashSet<IPlugin>();

			foreach (IPlugin plugin in ActivePlugins) {
				try {
					string pluginName = plugin.GetName();

					ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.PluginLoading, pluginName, plugin.GetVersion()));
					plugin.OnLoaded();
					ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.PluginLoaded, pluginName));
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
					invalidPlugins.Add(plugin);
				}
			}

			ImmutableHashSet<IPlugin> activePlugins = ActivePlugins.Except(invalidPlugins);

			if (activePlugins.Count == 0) {
				ActivePlugins = null;

				return false;
			}

			ActivePlugins = activePlugins;
			ASF.ArchiLogger.LogGenericInfo(Strings.PluginsWarning);

			return invalidPlugins.Count == 0;
		}

		internal static void OnASFInitModules(IReadOnlyDictionary<string, JToken> additionalConfigProperties = null) {
			if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
				return;
			}

			foreach (IASF plugin in ActivePlugins.OfType<IASF>()) {
				try {
					plugin.OnASFInit(additionalConfigProperties);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}
		}

		internal static async Task<string> OnBotCommand(Bot bot, ulong steamID, string message, string[] args) {
			if ((bot == null) || (steamID == 0) || string.IsNullOrEmpty(message) || (args == null) || (args.Length == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(args));

				return null;
			}

			if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
				return null;
			}

			foreach (IBotCommand plugin in ActivePlugins.OfType<IBotCommand>()) {
				string response;

				try {
					response = await plugin.OnBotCommand(bot, steamID, message, args).ConfigureAwait(false);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);

					continue;
				}

				if (!string.IsNullOrEmpty(response)) {
					return response;
				}
			}

			return null;
		}

		internal static void OnBotDestroy(Bot bot) {
			if (bot == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot));

				return;
			}

			if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
				return;
			}

			foreach (IBot plugin in ActivePlugins.OfType<IBot>()) {
				try {
					plugin.OnBotDestroy(bot);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}
		}

		internal static void OnBotDisconnected(Bot bot, EResult reason) {
			if (bot == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot));

				return;
			}

			foreach (IBotConnection plugin in ActivePlugins.OfType<IBotConnection>()) {
				try {
					plugin.OnBotDisconnected(bot, reason);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}
		}

		internal static void OnBotInit(Bot bot) {
			if (bot == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot));

				return;
			}

			if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
				return;
			}

			foreach (IBot plugin in ActivePlugins.OfType<IBot>()) {
				try {
					plugin.OnBotInit(bot);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}
		}

		internal static void OnBotInitModules(Bot bot, IReadOnlyDictionary<string, JToken> additionalConfigProperties = null) {
			if (bot == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot));

				return;
			}

			if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
				return;
			}

			foreach (IBotModules plugin in ActivePlugins.OfType<IBotModules>()) {
				try {
					plugin.OnBotInitModules(bot, additionalConfigProperties);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}
		}

		internal static void OnBotLoggedOn(Bot bot) {
			if (bot == null) {
				ASF.ArchiLogger.LogNullError(nameof(bot));

				return;
			}

			foreach (IBotConnection plugin in ActivePlugins.OfType<IBotConnection>()) {
				try {
					plugin.OnBotLoggedOn(bot);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}
		}

		internal static async Task<string> OnBotMessage(Bot bot, ulong steamID, string message) {
			if ((bot == null) || (steamID == 0) || string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(bot) + " || " + nameof(message));

				return null;
			}

			if ((ActivePlugins == null) || (ActivePlugins.Count == 0)) {
				return null;
			}

			foreach (IBotMessage plugin in ActivePlugins.OfType<IBotMessage>()) {
				string response;

				try {
					response = await plugin.OnBotMessage(bot, steamID, message).ConfigureAwait(false);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);

					continue;
				}

				if (!string.IsNullOrEmpty(response)) {
					return response;
				}
			}

			return null;
		}
	}
}
