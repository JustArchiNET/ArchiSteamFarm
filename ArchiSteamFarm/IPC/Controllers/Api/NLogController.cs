//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/NLog")]
	public sealed class NLogController : ArchiController {
		private static readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> ActiveLogWebSockets = new ConcurrentDictionary<WebSocket, SemaphoreSlim>();

		/// <summary>
		///     Fetches ASF log in realtime.
		/// </summary>
		/// <remarks>
		///     This API endpoint requires a websocket connection.
		/// </remarks>
		[HttpGet]
		[ProducesResponseType(typeof(IEnumerable<GenericResponse<string>>), 200)]
		public async Task<ActionResult> NLogGet() {
			if (!HttpContext.WebSockets.IsWebSocketRequest) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.WarningFailedWithError, nameof(HttpContext.WebSockets.IsWebSocketRequest) + ": " + HttpContext.WebSockets.IsWebSocketRequest)));
			}

			// From now on we can return only EmptyResult as the response stream is already being used by existing websocket connection

			try {
				using (WebSocket webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync().ConfigureAwait(false)) {
					SemaphoreSlim sendSemaphore = new SemaphoreSlim(1, 1);

					if (!ActiveLogWebSockets.TryAdd(webSocket, sendSemaphore)) {
						sendSemaphore.Dispose();

						return new EmptyResult();
					}

					try {
						// Push initial history if available
						if (ArchiKestrel.HistoryTarget != null) {
							// ReSharper disable once AccessToDisposedClosure - we're waiting for completion with Task.WhenAll(), we're not going to exit using block
							await Task.WhenAll(ArchiKestrel.HistoryTarget.ArchivedMessages.Select(archivedMessage => PostLoggedMessageUpdate(webSocket, sendSemaphore, archivedMessage))).ConfigureAwait(false);
						}

						while (webSocket.State == WebSocketState.Open) {
							WebSocketReceiveResult result = await webSocket.ReceiveAsync(new byte[0], CancellationToken.None).ConfigureAwait(false);

							if (result.MessageType != WebSocketMessageType.Close) {
								await webSocket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "You're not supposed to be sending any message but Close!", CancellationToken.None).ConfigureAwait(false);

								break;
							}

							await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None).ConfigureAwait(false);

							break;
						}
					} finally {
						if (ActiveLogWebSockets.TryRemove(webSocket, out SemaphoreSlim closedSemaphore)) {
							await closedSemaphore.WaitAsync().ConfigureAwait(false); // Ensure that our semaphore is truly closed by now
							closedSemaphore.Dispose();
						}
					}
				}
			} catch (WebSocketException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			}

			return new EmptyResult();
		}

		internal static async void OnNewHistoryEntry(object sender, HistoryTarget.NewHistoryEntryArgs newHistoryEntryArgs) {
			if ((sender == null) || (newHistoryEntryArgs == null)) {
				ASF.ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(newHistoryEntryArgs));

				return;
			}

			if (ActiveLogWebSockets.Count == 0) {
				return;
			}

			string json = JsonConvert.SerializeObject(new GenericResponse<string>(newHistoryEntryArgs.Message));
			await Task.WhenAll(ActiveLogWebSockets.Where(kv => kv.Key.State == WebSocketState.Open).Select(kv => PostLoggedJsonUpdate(kv.Key, kv.Value, json))).ConfigureAwait(false);
		}

		private static async Task PostLoggedJsonUpdate(WebSocket webSocket, SemaphoreSlim sendSemaphore, string json) {
			if ((webSocket == null) || (sendSemaphore == null) || string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogNullError(nameof(webSocket) + " || " + nameof(sendSemaphore) + " || " + nameof(json));

				return;
			}

			if (webSocket.State != WebSocketState.Open) {
				return;
			}

			await sendSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (webSocket.State != WebSocketState.Open) {
					return;
				}

				await webSocket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, true, CancellationToken.None).ConfigureAwait(false);
			} catch (WebSocketException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} finally {
				sendSemaphore.Release();
			}
		}

		private static async Task PostLoggedMessageUpdate(WebSocket webSocket, SemaphoreSlim sendSemaphore, string loggedMessage) {
			if ((webSocket == null) || (sendSemaphore == null) || string.IsNullOrEmpty(loggedMessage)) {
				ASF.ArchiLogger.LogNullError(nameof(webSocket) + " || " + nameof(sendSemaphore) + " || " + nameof(loggedMessage));

				return;
			}

			if (webSocket.State != WebSocketState.Open) {
				return;
			}

			string response = JsonConvert.SerializeObject(new GenericResponse<string>(loggedMessage));
			await PostLoggedJsonUpdate(webSocket, sendSemaphore, response).ConfigureAwait(false);
		}
	}
}
