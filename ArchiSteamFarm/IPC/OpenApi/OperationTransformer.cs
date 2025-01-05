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
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi.Models;

namespace ArchiSteamFarm.IPC.OpenApi;

#pragma warning disable CA1812 // False positive, the class is used internally
[UsedImplicitly]
internal sealed class OperationTransformer : IOpenApiOperationTransformer {
	public Task TransformAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken cancellationToken) {
		ArgumentNullException.ThrowIfNull(operation);
		ArgumentNullException.ThrowIfNull(context);

		if (context.Description.RelativePath?.StartsWith("Api", StringComparison.OrdinalIgnoreCase) == true) {
			operation.Security ??= new List<OpenApiSecurityRequirement>(1);

			operation.Security.Add(
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
		}

		return Task.CompletedTask;
	}
}
#pragma warning restore CA1812 // False positive, the class is used internally
