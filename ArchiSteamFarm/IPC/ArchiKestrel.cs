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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.IPC.Controllers.Api;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.IPC.OpenApi;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.NLog.Targets;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Storage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using NLog.Web;
using Scalar.AspNetCore;
using IPNetwork = System.Net.IPNetwork;

namespace ArchiSteamFarm.IPC;

internal static class ArchiKestrel {
	internal static bool IsRunning => WebApplication != null;

	internal static HistoryTarget? HistoryTarget { get; private set; }

	private static readonly SemaphoreSlim StateSemaphore = new(1, 1);

	private static WebApplication? WebApplication;

	internal static void OnNewHistoryTarget(HistoryTarget? historyTarget = null) {
		if (HistoryTarget != null) {
			HistoryTarget.NewHistoryEntry -= NLogController.OnNewHistoryEntry;
			HistoryTarget = null;
		}

		if (historyTarget != null) {
			historyTarget.NewHistoryEntry += NLogController.OnNewHistoryEntry;
			HistoryTarget = historyTarget;
		}
	}

	internal static async Task Restart() {
		await StateSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			await StopInternally().ConfigureAwait(false);
			await StartInternally().ConfigureAwait(false);
		} finally {
			StateSemaphore.Release();
		}
	}

	internal static async Task Start() {
		if (IsRunning) {
			return;
		}

		await StateSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			await StartInternally().ConfigureAwait(false);
		} finally {
			StateSemaphore.Release();
		}
	}

	internal static async Task Stop() {
		if (!IsRunning) {
			return;
		}

		await StateSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			await StopInternally().ConfigureAwait(false);
		} finally {
			StateSemaphore.Release();
		}
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "PathString is a primitive, it's unlikely to be trimmed to the best of our knowledge")]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL3000", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private static void ConfigureApp([SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")] ConfigurationManager configuration, IWebHostEnvironment environment, WebApplication app) {
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(environment);
		ArgumentNullException.ThrowIfNull(app);

		// The order of dependency injection is super important, doing things in wrong order will most likely break everything
		// https://docs.microsoft.com/aspnet/core/fundamentals/middleware

		// This one is easy, it's always in the beginning
		if (Debugging.IsUserDebugging) {
			app.UseDeveloperExceptionPage();
		}

		// Add support for proxies, this one comes usually after developer exception page, but could be before
		app.UseForwardedHeaders();

		// Add support for response caching - must be called before static files as we want to cache those as well
		if (ASF.GlobalConfig?.OptimizationMode != GlobalConfig.EOptimizationMode.MinMemoryUsage) {
			// As previously in services, we skip it if memory usage is super important for us
			app.UseResponseCaching();
		}

		// Add support for response compression - must be called before static files as we want to compress those as well
		app.UseResponseCompression();

		// It's not apparent when UsePathBase() should be called, but definitely before we get down to static files
		// TODO: Maybe eventually we can get rid of this, https://github.com/aspnet/AspNetCore/issues/5898
		PathString pathBase = configuration.GetSection("Kestrel").GetValue<PathString>("PathBase");

		if (!string.IsNullOrEmpty(pathBase) && (pathBase != "/")) {
			app.UsePathBase(pathBase);
		}

		// The default HTML file (usually index.html) is responsible for IPC GUI routing, so re-execute all non-API calls on /
		// This must be called before default files, because we don't know the exact file name that will be used for index page
		app.UseWhen(static context => !context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase), static appBuilder => appBuilder.UseStatusCodePagesWithReExecute("/"));

		if (!string.IsNullOrEmpty(environment.WebRootPath)) {
			// Add support for default root path redirection (GET / -> GET /index.html), must come before static files
			app.UseDefaultFiles();
		}

		// Add support for additional default files provided by plugins
		Dictionary<string, string> pluginPaths = new(StringComparer.Ordinal);

		foreach (IWebInterface plugin in PluginsCore.ActivePlugins.OfType<IWebInterface>()) {
			string physicalPath = plugin.PhysicalPath;

			if (string.IsNullOrEmpty(physicalPath)) {
				// Invalid path provided
				ASF.ArchiLogger.LogGenericError(Strings.FormatErrorObjectIsNull($"{nameof(physicalPath)} ({plugin.Name})"));

				continue;
			}

			string webPath = plugin.WebPath;

			if (string.IsNullOrEmpty(webPath)) {
				// Invalid path provided
				ASF.ArchiLogger.LogGenericError(Strings.FormatErrorObjectIsNull($"{nameof(webPath)} ({plugin.Name})"));

				continue;
			}

			if (!Path.IsPathRooted(physicalPath)) {
				// Relative path
				string? assemblyDirectory = Path.GetDirectoryName(plugin.GetType().Assembly.Location);

				if (string.IsNullOrEmpty(assemblyDirectory)) {
					throw new InvalidOperationException(nameof(assemblyDirectory));
				}

				physicalPath = Path.Combine(assemblyDirectory, physicalPath);
			}

			if (!Directory.Exists(physicalPath)) {
				// Non-existing path provided
				ASF.ArchiLogger.LogGenericWarning(Strings.FormatErrorIsInvalid($"{nameof(physicalPath)} ({plugin.Name})"));

				continue;
			}

			pluginPaths[physicalPath] = webPath;

			if (webPath != "/") {
				app.UseDefaultFiles(webPath);
			}
		}

		// Add support for additional static files from custom plugins (e.g. HTML, CSS and JS)
		foreach ((string physicalPath, string webPath) in pluginPaths) {
			StaticFileOptions options = new() {
				FileProvider = new PhysicalFileProvider(physicalPath),
				OnPrepareResponse = OnPrepareResponse
			};

			if (webPath != "/") {
				options.RequestPath = webPath;
			}

			app.UseStaticFiles(options);
		}

		if (!string.IsNullOrEmpty(environment.WebRootPath)) {
			// Add support for static files (e.g. HTML, CSS and JS from IPC GUI)
			app.UseStaticFiles(
				new StaticFileOptions {
					OnPrepareResponse = OnPrepareResponse
				}
			);
		}

		// Use routing for our API controllers, this should be called once we're done with all the static files mess
		app.UseRouting();

		// We want to protect our API with IPCPassword and additional security, this should be called after routing, so the middleware won't have to deal with API endpoints that do not exist
		app.UseWhen(static context => context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase), static appBuilder => appBuilder.UseMiddleware<ApiAuthenticationMiddleware>());

		// Add support for CORS policy in order to allow userscripts and other third-party integrations to communicate with ASF API
		string? ipcPassword = ASF.GlobalConfig != null ? ASF.GlobalConfig.IPCPassword : GlobalConfig.DefaultIPCPassword;

		if (!string.IsNullOrEmpty(ipcPassword)) {
			// We apply CORS policy only with IPCPassword set as an extra authentication measure
			app.UseCors();
		}

		// Add support for websockets that we use e.g. in /Api/NLog
		app.UseWebSockets();

		// Add support for output caching
		if (ASF.GlobalConfig?.OptimizationMode != GlobalConfig.EOptimizationMode.MinMemoryUsage) {
			app.UseOutputCache();
		}

		// Add additional endpoints provided by plugins
		foreach (IWebServiceProvider plugin in PluginsCore.ActivePlugins.OfType<IWebServiceProvider>()) {
			try {
				plugin.OnConfiguringEndpoints(app);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		// Finally register proper API endpoints once we're done with routing
		app.MapControllers();

		// Add support for OpenAPI, responsible for automatic API documentation generation, this should be on the end, once we're done with API
		IEndpointConventionBuilder openApi = app.MapOpenApi("/swagger/{documentName}/swagger.json");

		if (ASF.GlobalConfig?.OptimizationMode != GlobalConfig.EOptimizationMode.MinMemoryUsage) {
			openApi.CacheOutput();
		}

		// Add support for swagger UI, this should be after swagger, obviously
		app.MapScalarApiReference(
			"/swagger", static options => {
				options.DefaultFonts = false;
				options.OpenApiRoutePattern = $"/swagger/{SharedInfo.ASF}/swagger.json";
				options.Theme = ScalarTheme.Kepler;
				options.Title = $"{SharedInfo.AssemblyName} API";
			}
		);
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private static void ConfigureServices([SuppressMessage("ReSharper", "SuggestBaseTypeForParameter")] ConfigurationManager configuration, IServiceCollection services) {
		ArgumentNullException.ThrowIfNull(configuration);
		ArgumentNullException.ThrowIfNull(services);

		// The order of dependency injection is super important, doing things in wrong order will most likely break everything
		// https://docs.microsoft.com/aspnet/core/fundamentals/middleware

		// Prepare knownNetworks that we'll use in a second
		HashSet<string>? knownNetworksTexts = configuration.GetSection("Kestrel:KnownNetworks").Get<HashSet<string>>();

		HashSet<IPNetwork>? knownNetworks = null;

		if (knownNetworksTexts?.Count > 0) {
			// Use specified known networks
			knownNetworks = [];

			foreach (string knownNetworkText in knownNetworksTexts) {
				string[] addressParts = knownNetworkText.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);

				if ((addressParts.Length != 2) || !IPAddress.TryParse(addressParts[0], out IPAddress? ipAddress) || !byte.TryParse(addressParts[1], out byte prefixLength)) {
					ASF.ArchiLogger.LogGenericError(Strings.FormatErrorIsInvalid(nameof(knownNetworkText)));
					ASF.ArchiLogger.LogGenericDebug($"{nameof(knownNetworkText)}: {knownNetworkText}");

					continue;
				}

				knownNetworks.Add(new IPNetwork(ipAddress, prefixLength));
			}
		}

		// Add support for proxies
		services.Configure<ForwardedHeadersOptions>(options => {
				options.ForwardedHeaders = ForwardedHeaders.All;

				if (knownNetworks != null) {
					foreach (IPNetwork knownNetwork in knownNetworks) {
						options.KnownIPNetworks.Add(knownNetwork);
					}
				}
			}
		);

		// Add support for response caching
		if (ASF.GlobalConfig?.OptimizationMode != GlobalConfig.EOptimizationMode.MinMemoryUsage) {
			// We can skip it if memory usage is super important for us
			services.AddResponseCaching();
		}

		// Add support for response compression
		services.AddResponseCompression(static options => options.EnableForHttps = true);

		// Add support for CORS policy in order to allow userscripts and other third-party integrations to communicate with ASF API
		string? ipcPassword = ASF.GlobalConfig != null ? ASF.GlobalConfig.IPCPassword : GlobalConfig.DefaultIPCPassword;

		if (!string.IsNullOrEmpty(ipcPassword)) {
			// We apply CORS policy only with IPCPassword set as an extra authentication measure
			services.AddCors(static options => options.AddDefaultPolicy(static policyBuilder => policyBuilder.AllowAnyOrigin()));
		}

		// Add support for output caching
		if (ASF.GlobalConfig?.OptimizationMode != GlobalConfig.EOptimizationMode.MinMemoryUsage) {
			services.AddOutputCache();
		}

		// Add support for OpenAPI, responsible for automatic API documentation generation
		services.AddOpenApi(
			SharedInfo.ASF, static options => {
				options.AddDocumentTransformer<DocumentTransformer>();
				options.AddOperationTransformer<OperationTransformer>();
				options.AddSchemaTransformer<SchemaTransformer>();
			}
		);

		// Add support for optional health-checks
		services.AddHealthChecks();

		// Add support for additional services provided by plugins
		foreach (IWebServiceProvider plugin in PluginsCore.ActivePlugins.OfType<IWebServiceProvider>()) {
			try {
				plugin.OnConfiguringServices(services);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		services.ConfigureHttpJsonOptions(static options => {
				JsonSerializerOptions jsonSerializerOptions = Debugging.IsUserDebugging ? JsonUtilities.IndentedJsonSerializerOptions : JsonUtilities.DefaultJsonSerializerOptions;

				options.SerializerOptions.PropertyNamingPolicy = jsonSerializerOptions.PropertyNamingPolicy;
				options.SerializerOptions.TypeInfoResolver = jsonSerializerOptions.TypeInfoResolver;
				options.SerializerOptions.WriteIndented = jsonSerializerOptions.WriteIndented;
			}
		);

		// We need MVC for /Api, but we're going to use only a small subset of all available features
		IMvcBuilder mvc = services.AddControllers();

		// Add support for additional controllers provided by plugins
		HashSet<Assembly>? assemblies = PluginsCore.LoadAssemblies();

		if (assemblies != null) {
			foreach (Assembly assembly in assemblies) {
				mvc.AddApplicationPart(assembly);
			}
		}

		// Register discovered controllers
		mvc.AddControllersAsServices();

		// Modify default JSON options
		mvc.AddJsonOptions(static options => {
				JsonSerializerOptions jsonSerializerOptions = Debugging.IsUserDebugging ? JsonUtilities.IndentedJsonSerializerOptions : JsonUtilities.DefaultJsonSerializerOptions;

				options.JsonSerializerOptions.PropertyNamingPolicy = jsonSerializerOptions.PropertyNamingPolicy;
				options.JsonSerializerOptions.TypeInfoResolver = jsonSerializerOptions.TypeInfoResolver;
				options.JsonSerializerOptions.WriteIndented = jsonSerializerOptions.WriteIndented;
			}
		);
	}

	private static async Task<WebApplication> CreateWebApplication() {
		// Try to initialize to custom www folder first
		string? webRootPath = Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.WebsiteDirectory);

		if (!Directory.Exists(webRootPath)) {
			// Try to initialize to standard www folder next
			webRootPath = Path.Combine(AppContext.BaseDirectory, SharedInfo.WebsiteDirectory);

			if (!Directory.Exists(webRootPath)) {
				// Do not attempt to create a new directory, user has explicitly removed it
				webRootPath = null;
			}
		}

		// The order of dependency injection matters, pay attention to it
		WebApplicationBuilder builder = WebApplication.CreateEmptyBuilder(
			new WebApplicationOptions {
				ApplicationName = SharedInfo.AssemblyName,
				ContentRootPath = SharedInfo.HomeDirectory,
				WebRootPath = webRootPath
			}
		);

		// Enable NLog integration for logging
		builder.Logging.SetMinimumLevel(Debugging.IsUserDebugging ? LogLevel.Trace : LogLevel.Warning);
		builder.Logging.AddNLogWeb(new NLogAspNetCoreOptions { ShutdownOnDispose = false });

		// Check if custom config is available
		string absoluteConfigDirectory = Path.Combine(Directory.GetCurrentDirectory(), SharedInfo.ConfigDirectory);
		string customConfigPath = Path.Combine(absoluteConfigDirectory, SharedInfo.IPCConfigFile);
		bool customConfigExists = File.Exists(customConfigPath);

		if (customConfigExists) {
			if (Debugging.IsDebugConfigured) {
				try {
					string json = await File.ReadAllTextAsync(customConfigPath).ConfigureAwait(false);

					if (!string.IsNullOrEmpty(json)) {
						JsonNode? jsonNode = JsonNode.Parse(json);

						ASF.ArchiLogger.LogGenericDebug($"{SharedInfo.IPCConfigFile}: {jsonNode?.ToJsonText(true) ?? "null"}");
					}
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}

			// Set up custom config to be used
			builder.WebHost.UseConfiguration(new ConfigurationBuilder().SetBasePath(absoluteConfigDirectory).AddJsonFile(SharedInfo.IPCConfigFile, false, true).Build());
		}

		builder.WebHost.ConfigureKestrel(options => {
				options.AddServerHeader = false;

				if (customConfigExists) {
					// Use custom config for Kestrel configuration
					options.Configure(builder.Configuration.GetSection("Kestrel"));
				} else {
					// Use ASFB defaults for Kestrel
					options.ListenLocalhost(1242);
				}
			}
		);

		if (customConfigExists) {
			// User might be using HTTPS when providing custom config, use full implementation of Kestrel for that scenario
			builder.WebHost.UseKestrel();
		} else {
			// We don't need extra features when not using custom config
			builder.WebHost.UseKestrelCore();
		}

		ConfigureServices(builder.Configuration, builder.Services);

		WebApplication result = builder.Build();

		ConfigureApp(builder.Configuration, builder.Environment, result);

		return result;
	}

	private static void OnPrepareResponse(StaticFileResponseContext context) {
		ArgumentNullException.ThrowIfNull(context);

		if (context.File is not { Exists: true, IsDirectory: false } || string.IsNullOrEmpty(context.File.Name)) {
			return;
		}

		string extension = Path.GetExtension(context.File.Name);

		CacheControlHeaderValue cacheControl = new();

		switch (extension.ToUpperInvariant()) {
			case ".CSS" or ".JS":
				// Add support for SRI-protected static files
				// SRI requires from us to notify the caller (especially proxy) to avoid modifying the data
				cacheControl.NoTransform = true;

				goto default;
			default:
				// Instruct the caller to always ask us first about every file it requests
				// Contrary to the name, this doesn't prevent client from caching, but rather informs it that it must verify with us first that their cache is still up-to-date
				// This is used to handle ASF and user updates to WWW root, we don't want the client to ever use outdated scripts
				cacheControl.NoCache = true;

				// All static files are public by definition, we don't have any authorization here
				cacheControl.Public = true;

				break;
		}

		ResponseHeaders headers = context.Context.Response.GetTypedHeaders();

		headers.CacheControl = cacheControl;
	}

	private static async Task StartInternally() {
		if (WebApplication != null) {
			return;
		}

		ASF.ArchiLogger.LogGenericInfo(Strings.IPCStarting);

		// Init history logger for /Api/Log usage
		Logging.InitHistoryLogger();

		WebApplication webApplication = await CreateWebApplication().ConfigureAwait(false);

		try {
			// Start the server
			await webApplication.StartAsync().ConfigureAwait(false);
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			await webApplication.DisposeAsync().ConfigureAwait(false);

			return;
		}

		WebApplication = webApplication;

		ASF.ArchiLogger.LogGenericInfo(Strings.IPCReady);
	}

	private static async Task StopInternally() {
		if (WebApplication == null) {
			return;
		}

		await WebApplication.StopAsync().ConfigureAwait(false);
		await WebApplication.DisposeAsync().ConfigureAwait(false);

		WebApplication = null;
	}
}
