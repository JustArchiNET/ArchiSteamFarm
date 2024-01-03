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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Plugins;
using ArchiSteamFarm.Plugins.Interfaces;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Headers;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using IPNetwork = Microsoft.AspNetCore.HttpOverrides.IPNetwork;

namespace ArchiSteamFarm.IPC;

internal sealed class Startup {
	private readonly IConfiguration Configuration;

	public Startup(IConfiguration configuration) {
		ArgumentNullException.ThrowIfNull(configuration);

		Configuration = configuration;
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "PathString is a primitive, it's unlikely to be trimmed to the best of our knowledge")]
	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL3000", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	[UsedImplicitly]
	public void Configure(IApplicationBuilder app, IWebHostEnvironment env) {
		ArgumentNullException.ThrowIfNull(app);
		ArgumentNullException.ThrowIfNull(env);

		// The order of dependency injection is super important, doing things in wrong order will break everything
		// https://docs.microsoft.com/aspnet/core/fundamentals/middleware

		// This one is easy, it's always in the beginning
		if (Debugging.IsUserDebugging) {
			app.UseDeveloperExceptionPage();
		}

		// Add support for proxies, this one comes usually after developer exception page, but could be before
		app.UseForwardedHeaders();

		if (ASF.GlobalConfig?.OptimizationMode != GlobalConfig.EOptimizationMode.MinMemoryUsage) {
			// Add support for response caching - must be called before static files as we want to cache those as well
			app.UseResponseCaching();
		}

		// Add support for response compression - must be called before static files as we want to compress those as well
		app.UseResponseCompression();

		// It's not apparent when UsePathBase() should be called, but definitely before we get down to static files
		// TODO: Maybe eventually we can get rid of this, https://github.com/aspnet/AspNetCore/issues/5898
		PathString pathBase = Configuration.GetSection("Kestrel").GetValue<PathString>("PathBase");

		if (!string.IsNullOrEmpty(pathBase) && (pathBase != "/")) {
			app.UsePathBase(pathBase);
		}

		// The default HTML file (usually index.html) is responsible for IPC GUI routing, so re-execute all non-API calls on /
		// This must be called before default files, because we don't know the exact file name that will be used for index page
		app.UseWhen(static context => !context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase), static appBuilder => appBuilder.UseStatusCodePagesWithReExecute("/"));

		// Add support for default root path redirection (GET / -> GET /index.html), must come before static files
		app.UseDefaultFiles();

		Dictionary<string, string> pluginPaths = new(StringComparer.Ordinal);

		if (PluginsCore.ActivePlugins?.Count > 0) {
			foreach (IWebInterface plugin in PluginsCore.ActivePlugins.OfType<IWebInterface>()) {
				if (string.IsNullOrEmpty(plugin.PhysicalPath) || string.IsNullOrEmpty(plugin.WebPath)) {
					// Invalid path provided
					continue;
				}

				string physicalPath = plugin.PhysicalPath;

				if (!Path.IsPathRooted(physicalPath)) {
					// Relative path
					string? assemblyDirectory = Path.GetDirectoryName(plugin.GetType().Assembly.Location);

					if (string.IsNullOrEmpty(assemblyDirectory)) {
						// Invalid path provided
						continue;
					}

					physicalPath = Path.Combine(assemblyDirectory, plugin.PhysicalPath);
				}

				if (!Directory.Exists(physicalPath)) {
					// Non-existing path provided
					continue;
				}

				pluginPaths[physicalPath] = plugin.WebPath;

				if (plugin.WebPath != "/") {
					app.UseDefaultFiles(plugin.WebPath);
				}
			}
		}

		// Add support for static files from custom plugins (e.g. HTML, CSS and JS)
		foreach ((string physicalPath, string webPath) in pluginPaths) {
			app.UseStaticFiles(
				new StaticFileOptions {
					FileProvider = new PhysicalFileProvider(physicalPath),
					OnPrepareResponse = OnPrepareResponse,
					RequestPath = webPath
				}
			);
		}

		// Add support for static files (e.g. HTML, CSS and JS from IPC GUI)
		app.UseStaticFiles(
			new StaticFileOptions {
				OnPrepareResponse = OnPrepareResponse
			}
		);

		// Use routing for our API controllers, this should be called once we're done with all the static files mess
		app.UseRouting();

		// We want to protect our API with IPCPassword and additional security, this should be called after routing, so the middleware won't have to deal with API endpoints that do not exist
		app.UseWhen(static context => context.Request.Path.StartsWithSegments("/Api", StringComparison.OrdinalIgnoreCase), static appBuilder => appBuilder.UseMiddleware<ApiAuthenticationMiddleware>());

		string? ipcPassword = ASF.GlobalConfig != null ? ASF.GlobalConfig.IPCPassword : GlobalConfig.DefaultIPCPassword;

		if (!string.IsNullOrEmpty(ipcPassword)) {
			// We want to apply CORS policy in order to allow userscripts and other third-party integrations to communicate with ASF API, this should be called before response compression, but can't be due to how our flow works
			// We apply CORS policy only with IPCPassword set as an extra authentication measure
			app.UseCors();
		}

		// Add support for websockets that we use e.g. in /Api/NLog
		app.UseWebSockets();

		// Finally register proper API endpoints once we're done with routing
		app.UseEndpoints(static endpoints => endpoints.MapControllers());

		// Add support for swagger, responsible for automatic API documentation generation, this should be on the end, once we're done with API
		app.UseSwagger();

		// Add support for swagger UI, this should be after swagger, obviously
		app.UseSwaggerUI(
			static options => {
				options.DisplayRequestDuration();
				options.EnableDeepLinking();
				options.ShowExtensions();
				options.SwaggerEndpoint($"{SharedInfo.ASF}/swagger.json", $"{SharedInfo.ASF} API");
			}
		);
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "HashSet<string> isn't a primitive, but we widely use the required features everywhere and it's unlikely to be trimmed to the best of our knowledge")]
	public void ConfigureServices(IServiceCollection services) {
		ArgumentNullException.ThrowIfNull(services);

		// The order of dependency injection is super important, doing things in wrong order will break everything
		// Order in Configure() method is a good start

		// Prepare knownNetworks that we'll use in a second
		HashSet<string>? knownNetworksTexts = Configuration.GetSection("Kestrel:KnownNetworks").Get<HashSet<string>>();

		HashSet<IPNetwork>? knownNetworks = null;

		if (knownNetworksTexts?.Count > 0) {
			// Use specified known networks
			knownNetworks = new HashSet<IPNetwork>();

			foreach (string knownNetworkText in knownNetworksTexts) {
				string[] addressParts = knownNetworkText.Split('/', 3, StringSplitOptions.RemoveEmptyEntries);

				if ((addressParts.Length != 2) || !IPAddress.TryParse(addressParts[0], out IPAddress? ipAddress) || !byte.TryParse(addressParts[1], out byte prefixLength)) {
					ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(knownNetworkText)));
					ASF.ArchiLogger.LogGenericDebug($"{nameof(knownNetworkText)}: {knownNetworkText}");

					continue;
				}

				knownNetworks.Add(new IPNetwork(ipAddress, prefixLength));
			}
		}

		// Add support for proxies
		services.Configure<ForwardedHeadersOptions>(
			options => {
				options.ForwardedHeaders = ForwardedHeaders.All;

				if (knownNetworks != null) {
					foreach (IPNetwork knownNetwork in knownNetworks) {
						options.KnownNetworks.Add(knownNetwork);
					}
				}
			}
		);

		if (ASF.GlobalConfig?.OptimizationMode != GlobalConfig.EOptimizationMode.MinMemoryUsage) {
			// Add support for response caching
			services.AddResponseCaching();
		}

		// Add support for response compression
		services.AddResponseCompression(static options => options.EnableForHttps = true);

		string? ipcPassword = ASF.GlobalConfig != null ? ASF.GlobalConfig.IPCPassword : GlobalConfig.DefaultIPCPassword;

		if (!string.IsNullOrEmpty(ipcPassword)) {
			// We want to apply CORS policy in order to allow userscripts and other third-party integrations to communicate with ASF API
			// We apply CORS policy only with IPCPassword set as an extra authentication measure
			services.AddCors(static options => options.AddDefaultPolicy(static policyBuilder => policyBuilder.AllowAnyOrigin()));
		}

		// Add support for swagger, responsible for automatic API documentation generation
		services.AddSwaggerGen(
			static options => {
				options.AddSecurityDefinition(
					nameof(GlobalConfig.IPCPassword), new OpenApiSecurityScheme {
						Description = $"{nameof(GlobalConfig.IPCPassword)} authentication using request headers. Check {SharedInfo.ProjectURL}/wiki/IPC#authentication for more info.",
						In = ParameterLocation.Header,
						Name = ApiAuthenticationMiddleware.HeadersField,
						Type = SecuritySchemeType.ApiKey
					}
				);

				options.AddSecurityRequirement(
					new OpenApiSecurityRequirement {
						{
							new OpenApiSecurityScheme {
								Reference = new OpenApiReference {
									Id = nameof(GlobalConfig.IPCPassword),
									Type = ReferenceType.SecurityScheme
								}
							},

							Array.Empty<string>()
						}
					}
				);

				// We require custom schema IDs due to conflicting type names, choosing the proper one is tricky as there is no good answer and any kind of convention has a potential to create conflict
				// FullName and Name both do, ToString() for unknown to me reason doesn't, and I don't have courage to call our WebUtilities.GetUnifiedName() better than what .NET ships with (because it isn't)
				// Let's use ToString() until we find a good enough reason to change it, also, the name must pass ^[a-zA-Z0-9.-_]+$ regex
				options.CustomSchemaIds(static type => type.ToString().Replace('+', '-'));

				options.EnableAnnotations(true, true);

				options.SchemaFilter<CustomAttributesSchemaFilter>();
				options.SchemaFilter<EnumSchemaFilter>();

				options.SwaggerDoc(
					SharedInfo.ASF, new OpenApiInfo {
						Contact = new OpenApiContact {
							Name = SharedInfo.GithubRepo,
							Url = new Uri(SharedInfo.ProjectURL)
						},

						License = new OpenApiLicense {
							Name = SharedInfo.LicenseName,
							Url = new Uri(SharedInfo.LicenseURL)
						},

						Title = $"{SharedInfo.AssemblyName} API",
						Version = SharedInfo.Version.ToString()
					}
				);

				string xmlDocumentationFile = Path.Combine(AppContext.BaseDirectory, SharedInfo.AssemblyDocumentation);

				if (File.Exists(xmlDocumentationFile)) {
					options.IncludeXmlComments(xmlDocumentationFile);
				}
			}
		);

		// Add support for Newtonsoft.Json in swagger, this one must be executed after AddSwaggerGen()
		services.AddSwaggerGenNewtonsoftSupport();

		// We need MVC for /Api, but we're going to use only a small subset of all available features
		IMvcBuilder mvc = services.AddControllers();

		// Add support for controllers declared in custom plugins
		if (PluginsCore.ActivePlugins?.Count > 0) {
			HashSet<Assembly>? assemblies = PluginsCore.LoadAssemblies();

			if (assemblies != null) {
				foreach (Assembly assembly in assemblies) {
					mvc.AddApplicationPart(assembly);
				}
			}
		}

		mvc.AddControllersAsServices();

		mvc.AddNewtonsoftJson(
			static options => {
				// Fix default contract resolver to use original names and not a camel case
				options.SerializerSettings.ContractResolver = new DefaultContractResolver();

				if (Debugging.IsUserDebugging) {
					options.SerializerSettings.Formatting = Formatting.Indented;
				}
			}
		);
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
}
