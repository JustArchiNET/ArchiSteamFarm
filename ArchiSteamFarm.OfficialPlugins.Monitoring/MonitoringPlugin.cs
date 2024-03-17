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
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.OfficialPlugins.Monitoring.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.Monitoring;

[Export(typeof(IPlugin))]
[SuppressMessage("ReSharper", "MemberCanBeFileLocal")]
internal sealed class MonitoringPlugin : OfficialPlugin, IWebServiceProvider, IGitHubPluginUpdates, IASF, IDisposable {
	[JsonInclude]
	[Required]
	public override string Name => nameof(MonitoringPlugin);

	[JsonInclude]
	[Required]
	public override Version Version => typeof(MonitoringPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	private bool Enabled => (ASF.GlobalConfig?.IPC ?? false) && (Config?.Enabled ?? MonitoringConfig.DefaultEnabled);

	private const string MeterName = nameof(ArchiSteamFarm);

	private const string MetricNamePrefix = "asf";

	private Meter? Meter;

	public void OnConfiguringServices(IServiceCollection services) {
		ArgumentNullException.ThrowIfNull(services);

		if (!Enabled) {
			return;
		}

		services.AddOpenTelemetry().WithMetrics(
			static builder => {
				builder.AddPrometheusExporter(static config => config.ScrapeEndpointPath = "/Api/metrics");
				builder.AddMeter("Microsoft.AspNetCore.Hosting");
				builder.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
				builder.AddMeter(MeterName);

				builder.AddView(
					"http.server.request.duration",
					new ExplicitBucketHistogramConfiguration {
						Boundaries = [0, 0.005, 0.01, 0.025, 0.05, 0.075, 0.1, 0.25, 0.5, 0.75, 1, 2.5, 5, 7.5, 10]
					}
				);
			}
		);
	}

	public void OnConfiguringApplication(IApplicationBuilder app) {
		ArgumentNullException.ThrowIfNull(app);

		if (!Enabled) {
			return;
		}

		app.UseEndpoints(static builder => builder.MapPrometheusScrapingEndpoint());
	}

	[JsonInclude]
	private MonitoringConfig? Config { get; set; }

	public Task OnASFInit(IReadOnlyDictionary<string, JsonElement>? additionalConfigProperties = null) {
		if (additionalConfigProperties == null) {
			return Task.CompletedTask;
		}

		MonitoringConfig? config = null;

		foreach ((string configProperty, JsonElement configValue) in additionalConfigProperties) {
			try {
				config = configProperty switch {
					nameof(Monitoring) => configValue.Deserialize<MonitoringConfig>(),
					_ => config
				};
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				ASF.ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.PluginDisabledInConfig, nameof(MonitoringPlugin)));

				return Task.CompletedTask;
			}
		}

		config ??= new MonitoringConfig();

		if (!Enabled) {
			ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginDisabledInConfig, nameof(MonitoringPlugin)));

			return Task.CompletedTask;
		}

		Config = config;

		ASF.ArchiLogger.LogGenericInfo(string.Format(CultureInfo.CurrentCulture, Strings.PluginInitializedAndEnabled, nameof(MonitoringPlugin)));

		return Task.CompletedTask;
	}

	public override Task OnLoaded() {
		Utilities.WarnAboutIncompleteTranslation(Strings.ResourceManager);

		Meter = new Meter(MeterName, Version.ToString());

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_memory_usage",
			static () => GC.GetTotalMemory(false) / 1024,
			description: "Current memory usage of ASF"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_ipc_banned_ips",
			static () => ApiAuthenticationMiddleware.GetCurrentlyBannedIPs().Count(),
			description: "Number of IP addresses currently banned by ASFs IPC module"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_active_plugins",
			static () => PluginsCore.ActivePlugins.Count,
			description: "Number of plugins currently loaded in ASF"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bots", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? ImmutableHashSet<Bot>.Empty;

				return new List<Measurement<int>>(5) {
					new(bots.Count, new KeyValuePair<string, object?>(TagNames.BotState, "configured")),
					new(bots.Where(static bot => bot.IsConnectedAndLoggedOn).Count(), new KeyValuePair<string, object?>(TagNames.BotState, "online")),
					new(bots.Where(static bot => !bot.IsConnectedAndLoggedOn).Count(), new KeyValuePair<string, object?>(TagNames.BotState, "offline")),
					new(bots.Where(static bot => bot.CardsFarmer.NowFarming).Count(), new KeyValuePair<string, object?>(TagNames.BotState, "farming"))
				};
			},
			description: "Number of bots that are currently loaded in ASF"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_friends", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? ImmutableHashSet<Bot>.Empty;

				return bots.Where(static bot => bot.IsConnectedAndLoggedOn).Select(static bot => new Measurement<int>(bot.SteamFriends.GetFriendCount(), new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName)));
			},
			description: "Number of friends each bot has on Steam"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_clans", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? ImmutableHashSet<Bot>.Empty;

				return bots.Where(static bot => bot.IsConnectedAndLoggedOn).Select(static bot => new Measurement<int>(bot.SteamFriends.GetClanCount(), new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName)));
			},
			description: "Number of Steam groups each bot is in"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_farming_minutes_remaining", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? ImmutableHashSet<Bot>.Empty;

				return bots.Select(static bot => new Measurement<double>(bot.CardsFarmer.TimeRemaining.TotalMinutes, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName)));
			},
			description: "Approximate number of minutes remaining until each bot has finished farming all cards"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_heartbeat_failures", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? ImmutableHashSet<Bot>.Empty;

				return bots.Select(static bot => new Measurement<byte>(bot.HeartBeatFailures, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName)));
			},
			description: "Number of times a bot has failed to reach Steam servers"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_wallet_balance", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? ImmutableHashSet<Bot>.Empty;

				return bots.Where(static bot => bot.WalletCurrency != ECurrencyCode.Invalid).Select(static bot => new Measurement<long>(bot.WalletBalance, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.CurrencyCode, bot.WalletCurrency.ToString())));
			},
			description: "Current Steam wallet balance of each bot"
		);

		_ = Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_bgr_keys_remaining", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? ImmutableHashSet<Bot>.Empty;

				return bots.Select(static bot => new Measurement<long>(bot.GamesToRedeemInBackgroundCount, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName)));
			},
			description: "Remaining games to redeem in background per bot"
		);

		return Task.CompletedTask;
	}

	/// <inheritdoc />
	public string RepositoryName => SharedInfo.GithubRepo;

	public void Dispose() => Meter?.Dispose();
}
