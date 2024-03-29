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
using ArchiSteamFarm.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Metrics;
using SteamKit2;

namespace ArchiSteamFarm.OfficialPlugins.Monitoring;

[Export(typeof(IPlugin))]
[SuppressMessage("ReSharper", "MemberCanBeFileLocal")]
internal sealed class MonitoringPlugin : OfficialPlugin, IWebServiceProvider, IGitHubPluginUpdates, IDisposable {
	private const string MeterName = SharedInfo.AssemblyName;

	private const string MetricNamePrefix = "asf";

	private static bool Enabled => ASF.GlobalConfig?.IPC ?? GlobalConfig.DefaultIPC;

	[JsonInclude]
	[Required]
	public override string Name => nameof(MonitoringPlugin);

	public string RepositoryName => SharedInfo.GithubRepo;

	[JsonInclude]
	[Required]
	public override Version Version => typeof(MonitoringPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	private Meter? Meter;

	public void Dispose() => Meter?.Dispose();

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
				ICollection<Bot> bots = Bot.Bots?.Values ?? Array.Empty<Bot>();

				return new List<Measurement<int>>(4) {
					new(bots.Count, new KeyValuePair<string, object?>(TagNames.BotState, "configured")),
					new(bots.Count(static bot => bot.IsConnectedAndLoggedOn), new KeyValuePair<string, object?>(TagNames.BotState, "online")),
					new(bots.Count(static bot => !bot.IsConnectedAndLoggedOn), new KeyValuePair<string, object?>(TagNames.BotState, "offline")),
					new(bots.Count(static bot => bot.CardsFarmer.NowFarming), new KeyValuePair<string, object?>(TagNames.BotState, "farming"))
				};
			},
			description: "Number of bots that are currently loaded in ASF"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_friends", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? Array.Empty<Bot>();

				return bots.Where(static bot => bot.IsConnectedAndLoggedOn).Select(static bot => new Measurement<int>(bot.SteamFriends.GetFriendCount(), new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Number of friends each bot has on Steam"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_clans", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? Array.Empty<Bot>();

				return bots.Where(static bot => bot.IsConnectedAndLoggedOn).Select(static bot => new Measurement<int>(bot.SteamFriends.GetClanCount(), new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Number of Steam groups each bot is in"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_farming_minutes_remaining", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? Array.Empty<Bot>();

				return bots.Select(static bot => new Measurement<double>(bot.CardsFarmer.TimeRemaining.TotalMinutes, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Approximate number of minutes remaining until each bot has finished farming all cards"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_heartbeat_failures", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? Array.Empty<Bot>();

				return bots.Select(static bot => new Measurement<byte>(bot.HeartBeatFailures, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Number of times a bot has failed to reach Steam servers"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_wallet_balance", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? Array.Empty<Bot>();

				return bots.Where(static bot => bot.WalletCurrency != ECurrencyCode.Invalid).Select(static bot => new Measurement<long>(bot.WalletBalance, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID), new KeyValuePair<string, object?>(TagNames.CurrencyCode, bot.WalletCurrency.ToString())));
			},
			description: "Current Steam wallet balance of each bot"
		);

		Meter.CreateObservableGauge(
			$"{MetricNamePrefix}_bot_bgr_keys_remaining", static () => {
				ICollection<Bot> bots = Bot.Bots?.Values ?? Array.Empty<Bot>();

				return bots.Select(static bot => new Measurement<long>(bot.GamesToRedeemInBackgroundCount, new KeyValuePair<string, object?>(TagNames.BotName, bot.BotName), new KeyValuePair<string, object?>(TagNames.SteamID, bot.SteamID)));
			},
			description: "Remaining games to redeem in background per bot"
		);
	}
}
