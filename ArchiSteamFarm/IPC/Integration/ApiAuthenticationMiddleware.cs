//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace ArchiSteamFarm.IPC.Integration {
	[SuppressMessage("ReSharper", "ClassNeverInstantiated.Global")]
	internal sealed class ApiAuthenticationMiddleware {
		internal const string HeadersField = "Authentication";

		private const byte FailedAuthorizationsCooldownInHours = 1;
		private const byte MaxFailedAuthorizationAttempts = 5;

		private static readonly SemaphoreSlim AuthorizationSemaphore = new SemaphoreSlim(1, 1);
		private static readonly ConcurrentDictionary<IPAddress, byte> FailedAuthorizations = new ConcurrentDictionary<IPAddress, byte>();

		private static Timer? ClearFailedAuthorizationsTimer;

		private readonly RequestDelegate Next;

		public ApiAuthenticationMiddleware(RequestDelegate next) {
			Next = next ?? throw new ArgumentNullException(nameof(next));

			lock (FailedAuthorizations) {
				ClearFailedAuthorizationsTimer ??= new Timer(
					e => FailedAuthorizations.Clear(),
					null,
					TimeSpan.FromHours(FailedAuthorizationsCooldownInHours), // Delay
					TimeSpan.FromHours(FailedAuthorizationsCooldownInHours) // Period
				);
			}
		}

		[PublicAPI]
		public async Task InvokeAsync(HttpContext context) {
			if (context == null) {
				throw new ArgumentNullException(nameof(context));
			}

			HttpStatusCode authenticationStatus = await GetAuthenticationStatus(context).ConfigureAwait(false);

			if (authenticationStatus != HttpStatusCode.OK) {
				await context.Response.Generate(authenticationStatus).ConfigureAwait(false);

				return;
			}

			await Next(context).ConfigureAwait(false);
		}

		private static async Task<HttpStatusCode> GetAuthenticationStatus(HttpContext context) {
			if ((context == null) || (ClearFailedAuthorizationsTimer == null)) {
				throw new ArgumentNullException(nameof(context) + " || " + nameof(ClearFailedAuthorizationsTimer));
			}

			string? ipcPassword = ASF.GlobalConfig != null ? ASF.GlobalConfig.IPCPassword : GlobalConfig.DefaultIPCPassword;

			if (string.IsNullOrEmpty(ipcPassword)) {
				return HttpStatusCode.OK;
			}

			IPAddress clientIP = context.Connection.RemoteIpAddress;

			if (FailedAuthorizations.TryGetValue(clientIP, out byte attempts)) {
				if (attempts >= MaxFailedAuthorizationAttempts) {
					return HttpStatusCode.Forbidden;
				}
			}

			if (!context.Request.Headers.TryGetValue(HeadersField, out StringValues passwords) && !context.Request.Query.TryGetValue("password", out passwords)) {
				return HttpStatusCode.Unauthorized;
			}

			string? inputPassword = passwords.FirstOrDefault(password => !string.IsNullOrEmpty(password));

			if (string.IsNullOrEmpty(inputPassword)) {
				return HttpStatusCode.Unauthorized;
			}

			bool authorized = inputPassword == ipcPassword;

			await AuthorizationSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (FailedAuthorizations.TryGetValue(clientIP, out attempts)) {
					if (attempts >= MaxFailedAuthorizationAttempts) {
						return HttpStatusCode.Forbidden;
					}
				}

				if (!authorized) {
					FailedAuthorizations[clientIP] = FailedAuthorizations.TryGetValue(clientIP, out attempts) ? ++attempts : (byte) 1;
				}
			} finally {
				AuthorizationSemaphore.Release();
			}

			return authorized ? HttpStatusCode.OK : HttpStatusCode.Unauthorized;
		}
	}
}
