// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2022-2024 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace ArchiSteamFarm.IPC.OpenApi;

#pragma warning disable CA1812 // False positive, the class is used internally
[UsedImplicitly]
internal sealed class DocumentTransformer : IOpenApiDocumentTransformer {
	public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(context);

		document.Info ??= new OpenApiInfo();
		document.Info.Title = $"{SharedInfo.AssemblyName} API";
		document.Info.Version = SharedInfo.Version.ToString();

		document.Info.Contact ??= new OpenApiContact();
		document.Info.Contact.Name = SharedInfo.GithubRepo;
		document.Info.Contact.Url = new Uri(SharedInfo.ProjectURL);

		document.Info.License ??= new OpenApiLicense();
		document.Info.License.Name = SharedInfo.LicenseName;
		document.Info.License.Url = new Uri(SharedInfo.LicenseURL);

		document.Components ??= new OpenApiComponents();
		document.Components.SecuritySchemes ??= new Dictionary<string, OpenApiSecurityScheme>(1);

		document.Components.SecuritySchemes.Add(
			nameof(GlobalConfig.IPCPassword), new OpenApiSecurityScheme {
				Description = $"{nameof(GlobalConfig.IPCPassword)} authentication using request headers. Check {SharedInfo.ProjectURL}/wiki/IPC#authentication for more info.",
				In = ParameterLocation.Header,
				Name = ApiAuthenticationMiddleware.HeadersField,
				Type = SecuritySchemeType.ApiKey
			}
		);

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // False positive, the class is used internally
