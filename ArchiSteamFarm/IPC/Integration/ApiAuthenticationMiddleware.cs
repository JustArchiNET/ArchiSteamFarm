//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;

namespace ArchiSteamFarm.IPC.Integration {
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	internal sealed class ApiAuthenticationMiddleware {
		internal const string HeadersField = "Authentication";

		private const byte FailedAuthorizationsCooldownInHours = 1;
		private const byte MaxFailedAuthorizationAttempts = 5;

		private static readonly SemaphoreSlim AuthorizationSemaphore = new(1, 1);
		private static readonly ConcurrentDictionary<IPAddress, byte> FailedAuthorizations = new();

		private static Timer? ClearFailedAuthorizationsTimer;

		private readonly ForwardedHeadersOptions ForwardedHeadersOptions;
		private readonly RequestDelegate Next;

		public ApiAuthenticationMiddleware(RequestDelegate next, IOptions<ForwardedHeadersOptions> forwardedHeadersOptions) {
			Next = next ?? throw new ArgumentNullException(nameof(next));

			if (forwardedHeadersOptions == null) {
				throw new ArgumentNullException(nameof(forwardedHeadersOptions));
			}

			ForwardedHeadersOptions = forwardedHeadersOptions.Value ?? throw new InvalidOperationException(nameof(forwardedHeadersOptions));

			lock (FailedAuthorizations) {
				ClearFailedAuthorizationsTimer ??= new Timer(
					_ => FailedAuthorizations.Clear(),
					null,
					TimeSpan.FromHours(FailedAuthorizationsCooldownInHours), // Delay
					TimeSpan.FromHours(FailedAuthorizationsCooldownInHours) // Period
				);
			}
		}

		[PublicAPI]
#if NETFRAMEWORK
		public async Task InvokeAsync(HttpContext context, IOptions<MvcJsonOptions> jsonOptions) {
#else
		public async Task InvokeAsync(HttpContext context, IOptions<MvcNewtonsoftJsonOptions> jsonOptions) {
#endif
			if (context == null) {
				throw new ArgumentNullException(nameof(context));
			}

			if (jsonOptions == null) {
				throw new ArgumentNullException(nameof(jsonOptions));
			}

			(HttpStatusCode statusCode, bool permanent) = await GetAuthenticationStatus(context).ConfigureAwait(false);

			if (statusCode == HttpStatusCode.OK) {
				await Next(context).ConfigureAwait(false);

				return;
			}

			context.Response.StatusCode = (int) statusCode;

			StatusCodeResponse statusCodeResponse = new(statusCode, permanent);

			await context.Response.WriteJsonAsync(new GenericResponse<StatusCodeResponse>(false, statusCodeResponse), jsonOptions.Value.SerializerSettings).ConfigureAwait(false);
		}

		private async Task<(HttpStatusCode StatusCode, bool Permanent)> GetAuthenticationStatus(HttpContext context) {
			if (context == null) {
				throw new ArgumentNullException(nameof(context));
			}

			if (ClearFailedAuthorizationsTimer == null) {
				throw new InvalidOperationException(nameof(ClearFailedAuthorizationsTimer));
			}

			IPAddress? clientIP = context.Connection.RemoteIpAddress;

			if (clientIP == null) {
				throw new InvalidOperationException(nameof(clientIP));
			}

			string? ipcPassword = ASF.GlobalConfig != null ? ASF.GlobalConfig.IPCPassword : GlobalConfig.DefaultIPCPassword;

			if (string.IsNullOrEmpty(ipcPassword)) {
				if (IPAddress.IsLoopback(clientIP)) {
					return (HttpStatusCode.OK, true);
				}

				if (ForwardedHeadersOptions.KnownNetworks.Count == 0) {
					return (HttpStatusCode.Forbidden, true);
				}

				if (clientIP.IsIPv4MappedToIPv6) {
					IPAddress mappedClientIP = clientIP.MapToIPv4();

					if (ForwardedHeadersOptions.KnownNetworks.Any(network => network.Contains(mappedClientIP))) {
						return (HttpStatusCode.OK, true);
					}
				}

				return (ForwardedHeadersOptions.KnownNetworks.Any(network => network.Contains(clientIP)) ? HttpStatusCode.OK : HttpStatusCode.Forbidden, true);
			}

			if (FailedAuthorizations.TryGetValue(clientIP, out byte attempts)) {
				if (attempts >= MaxFailedAuthorizationAttempts) {
					return (HttpStatusCode.Forbidden, false);
				}
			}

			if (!context.Request.Headers.TryGetValue(HeadersField, out StringValues passwords) && !context.Request.Query.TryGetValue("password", out passwords)) {
				return (HttpStatusCode.Unauthorized, true);
			}

			string? inputPassword = passwords.FirstOrDefault(password => !string.IsNullOrEmpty(password));

			if (string.IsNullOrEmpty(inputPassword)) {
				return (HttpStatusCode.Unauthorized, true);
			}

			ArchiCryptoHelper.EHashingMethod ipcPasswordFormat = ASF.GlobalConfig != null ? ASF.GlobalConfig.IPCPasswordFormat : GlobalConfig.DefaultIPCPasswordFormat;

			string inputHash = ArchiCryptoHelper.Hash(ipcPasswordFormat, inputPassword);

			bool authorized = ipcPassword == inputHash;

			await AuthorizationSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (FailedAuthorizations.TryGetValue(clientIP, out attempts)) {
					if (attempts >= MaxFailedAuthorizationAttempts) {
						return (HttpStatusCode.Forbidden, false);
					}
				}

				if (!authorized) {
					FailedAuthorizations[clientIP] = FailedAuthorizations.TryGetValue(clientIP, out attempts) ? ++attempts : (byte) 1;
				}
			} finally {
				AuthorizationSemaphore.Release();
			}

			return (authorized ? HttpStatusCode.OK : HttpStatusCode.Unauthorized, true);
		}
	}
}
