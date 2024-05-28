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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Composition;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Steam;
using ArchiSteamFarm.Steam.Exchange;
using ArchiSteamFarm.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.Monitoring;

[Export(typeof(IPlugin))]
[SuppressMessage("ReSharper", "MemberCanBeFileLocal")]
internal sealed class MonitoringPlugin : OfficialPlugin, IDisposable, IOfficialGitHubPluginUpdates, IWebInterface, IWebServiceProvider, IBotTradeOfferResults {
	private const string MeterName = SharedInfo.AssemblyName;

	private const string MetricNamePrefix = "asf";

	private const string UnknownLabelValueFallback = "unknown";

	private static readonly Measurement<byte> BuildInfo = new(
		1,
		new KeyValuePair<string, object?>(TagNames.Version, SharedInfo.Version.ToString()),
		new KeyValuePair<string, object?>(TagNames.Variant, SharedInfo.BuildInfo.Variant)
	);

	private static readonly Measurement<byte> RuntimeInfo = new(
		1,
		new KeyValuePair<string, object?>(TagNames.Framework, OS.Framework ?? UnknownLabelValueFallback),
		new KeyValuePair<string, object?>(TagNames.Runtime, OS.Runtime ?? UnknownLabelValueFallback),
		new KeyValuePair<string, object?>(TagNames.OS, OS.Description ?? UnknownLabelValueFallback)
	);

	private static bool Enabled => ASF.GlobalConfig?.IPC ?? GlobalConfig.DefaultIPC;

	[JsonInclude]
	[Required]
	public override string Name => nameof(MonitoringPlugin);

	public string RepositoryName => SharedInfo.GithubRepo;

	[JsonInclude]
	[Required]
	public override Version Version => typeof(MonitoringPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	private readonly ConcurrentDictionary<Bot, TradeStatistics> TradeStatistics = new();

	private Meter? Meter;

	public void Dispose() => Meter?.Dispose();

	public Task OnBotTradeOfferResults(Bot bot, IReadOnlyCollection<ParseTradeResult> tradeResults) {
		ArgumentNullException.ThrowIfNull(bot);
		ArgumentNullException.ThrowIfNull(tradeResults);

		TradeStatistics statistics = TradeStatistics.GetOrAdd(bot, static _ => new TradeStatistics());

		foreach (ParseTradeResult result in tradeResults) {
			statistics.Include(result);
		}

		return Task.CompletedTask;
	}

	public void OnConfiguringEndpoints(IApplicationBuilder app) {
		ArgumentNullException.ThrowIfNull(app);

		if (!Enabled) {
			return;
		}

		app.UseEndpoints(static builder => builder.MapPrometheusScrapingEndpoint());
	}

	public void OnConfiguringServices(IServiceCollection services) {
		ArgumentNullException.ThrowIfNull(services);

		if (!Enabled) {
			return;
		}

		InitializeMeter();

		services.AddOpenTelemetry().WithMetrics(
			builder => {
				builder.AddPrometheusExporter(static config => config.ScrapeEndpointPath = "/Api/metrics");
				builder.AddRuntimeInstrumentation();
				builder.AddAspNetCoreInstrumentation();
				builder.AddHttpClientInstrumentation();
				builder.AddMeter(Meter.Name);
			}
		);
	}

	public override Task OnLoaded() => Task.CompletedTask;

	[MemberNotNull(nameof(Meter))]
	private void InitializeMeter() {
		if (Meter != null) {
			return;
		}

		Meter = new Meter(MeterName, Version.ToString());

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_build_info",
			static () => BuildInfo,
			description: "Build information about ASF in form of label values"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_runtime_info",
			static () => RuntimeInfo,
			description: "Runtime information about ASF in form of label values"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_ipc_banned_ips",
			static () => ApiAuthenticationMiddleware.GetCurrentlyBannedIPs().Count(),
			description: "Number of IP addresses currently banned by ASFs IPC module"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_active_plugins",
			static () => PluginsCore.ActivePluginsCount,
			description: "Number of plugins currently loaded in ASF"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bots", static () => {
				IEnumerable<Bot> bots = Bot.Bots?.Values ?? [];

				int onlineCount = 0;
				int offlineCount = 0;
				int farmingCount = 0;

				foreach (Bot bot in bots) {
					if (bot.IsConnectedAndLoggedOn) {
						onlineCount++;
					} else {
						offlineCount++;
					}

					if (bot.CardsFarmer.NowFarming) {
						farmingCount++;
					}
				}

				return new HashSet<Measurement<int>>(4) {
					new(onlineCount + offlineCount, new KeyValuePair<string, object?>(TagNames.BotState, "configured")),
					new(onlineCount, new KeyValuePair<string, object?>(TagNames.BotState, "online")),
					new(offlineCount, new KeyValuePair<string, object?>(TagNames.BotState, "offline")),
					new(farmingCount, new KeyValuePair<string, object?>(TagNames.BotState, "farming"))
				};
			},
			description: "Number of bots that are currently loaded in ASF"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_friends", static () => {
				IEnumerable<Bot> bots = Bot.Bots?.Values ?? [];

				return bots.Where(static bot => bot.IsConnectedAndLoggedOn).Select(static bot => new Measurement<int>(bot.SteamFriends.GetFriendCount(), new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Number of friends each bot has on Steam"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_clans", static () => {
				IEnumerable<Bot> bots = Bot.Bots?.Values ?? [];

				return bots.Where(static bot => bot.IsConnectedAndLoggedOn).Select(static bot => new Measurement<int>(bot.SteamFriends.GetClanCount(), new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Number of Steam groups each bot is in"
		);

		// Keep in mind that we use a unit here and the unit needs to be a suffix to the name
		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_farming_time_remaining_{Units.Minutes}", static () => {
				IEnumerable<Bot> bots = Bot.Bots?.Values ?? [];

				return bots.Where(static bot => bot.IsConnectedAndLoggedOn).Select(static bot => new Measurement<double>(bot.CardsFarmer.TimeRemaining.TotalMinutes, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			Units.Minutes,
			"Approximate number of minutes remaining until each bot has finished farming all cards"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_heartbeat_failures", static () => {
				IEnumerable<Bot> bots = Bot.Bots?.Values ?? [];

				return bots.Select(static bot => new Measurement<byte>(bot.HeartBeatFailures, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Number of times a bot has failed to reach Steam servers"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_wallet_balance", static () => {
				IEnumerable<Bot> bots = Bot.Bots?.Values ?? [];

				return bots.Where(static bot => bot.WalletCurrency != ECurrencyCode.Invalid).Select(static bot => new Measurement<long>(bot.WalletBalance, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID), new KeyValuePair<string, object?>(TagNames.CurrencyCode, bot.WalletCurrency.ToString())));
			},
			description: "Current Steam wallet balance of each bot"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_bgr_keys_remaining", static () => {
				IEnumerable<Bot> bots = Bot.Bots?.Values ?? [];

				return bots.Select(static bot => new Measurement<int>((int) bot.GamesToRedeemInBackgroundCount, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Remaining games to redeem in background per bot"
		);

		Meter.CreateObservableCounter(
			$"{MetricNamePrefix}_bot_trades", () => TradeStatistics.SelectMany<KeyValuePair<Bot, TradeStatistics>, Measurement<int>>(
				static kv => [
					new Measurement<int>(
						kv.Value.AcceptedOffers,
						new KeyValuePair<string, object?>(TagNames.BotName, kv.Key.BotName),
						new KeyValuePair<string, object?>(TagNames.SteamID, kv.Key.SteamID),
						new KeyValuePair<string, object?>(TagNames.TradeOfferResult, "accepted")
					),
					new Measurement<int>(
						kv.Value.RejectedOffers,
						new KeyValuePair<string, object?>(TagNames.BotName, kv.Key.BotName),
						new KeyValuePair<string, object?>(TagNames.SteamID, kv.Key.SteamID),
						new KeyValuePair<string, object?>(TagNames.TradeOfferResult, "rejected")
					),
					new Measurement<int>(
						kv.Value.IgnoredOffers,
						new KeyValuePair<string, object?>(TagNames.BotName, kv.Key.BotName),
						new KeyValuePair<string, object?>(TagNames.SteamID, kv.Key.SteamID),
						new KeyValuePair<string, object?>(TagNames.TradeOfferResult, "ignored")
					),
					new Measurement<int>(
						kv.Value.BlacklistedOffers,
						new KeyValuePair<string, object?>(TagNames.BotName, kv.Key.BotName),
						new KeyValuePair<string, object?>(TagNames.SteamID, kv.Key.SteamID),
						new KeyValuePair<string, object?>(TagNames.TradeOfferResult, "blacklisted")
					),
					new Measurement<int>(
						kv.Value.ConfirmedOffers,
						new KeyValuePair<string, object?>(TagNames.BotName, kv.Key.BotName),
						new KeyValuePair<string, object?>(TagNames.SteamID, kv.Key.SteamID),
						new KeyValuePair<string, object?>(TagNames.TradeOfferResult, "confirmed")
					)
				]
			),
			description: "Trade offers per bot and action taken by ASF"
		);

		Meter.CreateObservableCounter(
			$"{MetricNamePrefix}_bot_items_given", () => TradeStatistics.Select(static kv => new Measurement<int>(kv.Value.ItemsGiven, new KeyValuePair<string, object?>(TagNames.BotName, kv.Key.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, kv.Key.SteamID))),
			description: "Items given per bot"
		);

		Meter.CreateObservableCounter(
			$"{MetricNamePrefix}_bot_items_received", () => TradeStatistics.Select(static kv => new Measurement<int>(kv.Value.ItemsReceived, new KeyValuePair<string, object?>(TagNames.BotName, kv.Key.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, kv.Key.SteamID))),
			description: "Items received per bot"
		);
	}
}
