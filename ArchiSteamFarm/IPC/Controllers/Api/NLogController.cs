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

#if NETFRAMEWORK
using JustArchiNET.Madness;
#endif
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog.Targets;
using Microsoft.AspNetCore.Connections;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/NLog")]
	public sealed class NLogController : ArchiController {
		private static readonly ConcurrentDictionary<WebSocket, (SemaphoreSlim Semaphore, CancellationToken CancellationToken)> ActiveLogWebSockets = new();

		/// <summary>
		///     Fetches ASF log in realtime.
		/// </summary>
		/// <remarks>
		///     This API endpoint requires a websocket connection.
		/// </remarks>
		[HttpGet]
		[ProducesResponseType(typeof(IEnumerable<GenericResponse<string>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		public async Task<ActionResult> NLogGet(CancellationToken cancellationToken) {
			if (HttpContext == null) {
				throw new InvalidOperationException(nameof(HttpContext));
			}

			if (!HttpContext.WebSockets.IsWebSocketRequest) {
				return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.WarningFailedWithError!, nameof(HttpContext.WebSockets.IsWebSocketRequest) + ": " + HttpContext.WebSockets.IsWebSocketRequest)));
			}

			// From now on we can return only EmptyResult as the response stream is already being used by existing websocket connection

			try {
				using WebSocket webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false);

				SemaphoreSlim sendSemaphore = new(1, 1);

				if (!ActiveLogWebSockets.TryAdd(webSocket, (sendSemaphore, cancellationToken))) {
					sendSemaphore.Dispose();

					return new EmptyResult();
				}

				try {
					// Push initial history if available
					if (ArchiKestrel.HistoryTarget != null) {
						// ReSharper disable once AccessToDisposedClosure - we're waiting for completion with Task.WhenAll(), we're not going to exit using block
						await Task.WhenAll(ArchiKestrel.HistoryTarget.ArchivedMessages.Select(archivedMessage => PostLoggedMessageUpdate(webSocket, archivedMessage, sendSemaphore, cancellationToken))).ConfigureAwait(false);
					}

					while (webSocket.State == WebSocketState.Open) {
						WebSocketReceiveResult result = await webSocket.ReceiveAsync(Array.Empty<byte>(), cancellationToken).ConfigureAwait(false);

						if (result.MessageType != WebSocketMessageType.Close) {
							await webSocket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "You're not supposed to be sending any message but Close!", cancellationToken).ConfigureAwait(false);

							break;
						}

						await webSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "", cancellationToken).ConfigureAwait(false);

						break;
					}
				} finally {
					if (ActiveLogWebSockets.TryRemove(webSocket, out (SemaphoreSlim Semaphore, CancellationToken CancellationToken) entry)) {
						await entry.Semaphore.WaitAsync(CancellationToken.None).ConfigureAwait(false); // Ensure that our semaphore is truly closed by now
						entry.Semaphore.Dispose();
					}
				}
			} catch (ConnectionAbortedException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (OperationCanceledException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (WebSocketException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			}

			return new EmptyResult();
		}

		internal static async void OnNewHistoryEntry(object? sender, HistoryTarget.NewHistoryEntryArgs newHistoryEntryArgs) {
			if (newHistoryEntryArgs == null) {
				throw new ArgumentNullException(nameof(newHistoryEntryArgs));
			}

			if (ActiveLogWebSockets.IsEmpty) {
				return;
			}

			string json = JsonConvert.SerializeObject(new GenericResponse<string>(newHistoryEntryArgs.Message));

			await Task.WhenAll(ActiveLogWebSockets.Where(kv => kv.Key.State == WebSocketState.Open).Select(kv => PostLoggedJsonUpdate(kv.Key, json, kv.Value.Semaphore, kv.Value.CancellationToken))).ConfigureAwait(false);
		}

		private static async Task PostLoggedJsonUpdate(WebSocket webSocket, string json, SemaphoreSlim sendSemaphore, CancellationToken cancellationToken) {
			if (webSocket == null) {
				throw new ArgumentNullException(nameof(webSocket));
			}

			if (string.IsNullOrEmpty(json)) {
				throw new ArgumentNullException(nameof(json));
			}

			if (sendSemaphore == null) {
				throw new ArgumentNullException(nameof(sendSemaphore));
			}

			if (cancellationToken.IsCancellationRequested || (webSocket.State != WebSocketState.Open)) {
				return;
			}

			try {
				await sendSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
			} catch (OperationCanceledException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);

				return;
			}

			try {
#pragma warning disable CA1508 // False positive, webSocket state could change between our previous check and this one due to semaphore wait
				if (cancellationToken.IsCancellationRequested || (webSocket.State != WebSocketState.Open)) {
#pragma warning restore CA1508 // False positive, webSocket state could change between our previous check and this one due to semaphore wait
					return;
				}

				await webSocket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
			} catch (ConnectionAbortedException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (OperationCanceledException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (WebSocketException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} finally {
				sendSemaphore.Release();
			}
		}

		private static async Task PostLoggedMessageUpdate(WebSocket webSocket, string loggedMessage, SemaphoreSlim sendSemaphore, CancellationToken cancellationToken) {
			if (webSocket == null) {
				throw new ArgumentNullException(nameof(webSocket));
			}

			if (string.IsNullOrEmpty(loggedMessage)) {
				throw new ArgumentNullException(nameof(loggedMessage));
			}

			if (sendSemaphore == null) {
				throw new ArgumentNullException(nameof(sendSemaphore));
			}

			if (cancellationToken.IsCancellationRequested || (webSocket.State != WebSocketState.Open)) {
				return;
			}

			string response = JsonConvert.SerializeObject(new GenericResponse<string>(loggedMessage));

			await PostLoggedJsonUpdate(webSocket, response, sendSemaphore, cancellationToken).ConfigureAwait(false);
		}
	}
}
