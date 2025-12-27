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
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Integration;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace ArchiSteamFarm.IPC.OpenApi;

#pragma warning disable CA1812 // False positive, the class is used internally
[UsedImplicitly]
internal sealed class DocumentTransformer : IOpenApiDocumentTransformer {
	public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(context);

		document.Info.Title = $"{SharedInfo.AssemblyName} API";
		document.Info.Version = SharedInfo.Version.ToString();

		document.Info.Contact ??= new OpenApiContact();
		document.Info.Contact.Name = SharedInfo.GithubRepo;
		document.Info.Contact.Url = new Uri(SharedInfo.ProjectURL);

		document.Info.License ??= new OpenApiLicense();
		document.Info.License.Name = SharedInfo.LicenseName;
		document.Info.License.Url = new Uri(SharedInfo.LicenseURL);

		document.Components ??= new OpenApiComponents();
		document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>(1);

		document.Components.SecuritySchemes.Add(
			nameof(GlobalConfig.IPCPassword), new OpenApiSecurityScheme {
				Description = $"{nameof(GlobalConfig.IPCPassword)} authentication using request headers. Check {SharedInfo.ProjectURL}/wiki/IPC#authentication for more info.",
				In = ParameterLocation.Header,
				Name = ApiAuthenticationMiddleware.HeadersField,
				Type = SecuritySchemeType.ApiKey
			}
		);

		// Add limited info support for our NLog endpoint
		ApiDescription? nlogEndpont = context.DescriptionGroups.SelectMany(static group => group.Items).FirstOrDefault(static endpoint => (endpoint.HttpMethod == null) && (endpoint.RelativePath == "Api/NLog"));

		if (nlogEndpont != null) {
			OpenApiOperation operation = new() {
				Description = nlogEndpont.ActionDescriptor.EndpointMetadata.OfType<EndpointDescriptionAttribute>().FirstOrDefault()?.Description,

				Responses = new OpenApiResponses {
					{
						StatusCodes.Status101SwitchingProtocols.ToString(),

						new OpenApiResponse {
							Description = nameof(HttpStatusCode.SwitchingProtocols)
						}
					},

					{
						StatusCodes.Status400BadRequest.ToString(),

						new OpenApiResponse {
							Description = nameof(HttpStatusCode.BadRequest)
						}
					}
				},

				Summary = nlogEndpont.ActionDescriptor.EndpointMetadata.OfType<EndpointSummaryAttribute>().FirstOrDefault()?.Summary,

				Tags = new HashSet<OpenApiTagReference>(1) { new("NLog", document) }
			};

			document.Paths.Add(
				$"/{nlogEndpont.RelativePath}", new OpenApiPathItem {
					Operations = new Dictionary<HttpMethod, OpenApiOperation>(2) {
						{ HttpMethod.Connect, operation },

						// This is in fact incorrect, however, swagger ui does not display connect-only methods, so we'll add fake GET as well
						{ HttpMethod.Get, operation }
					}
				}
			);
		}

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // False positive, the class is used internally
