/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class IPC {
		internal static bool KeepRunning { get; private set; }

		private static readonly HttpListener HttpListener = new HttpListener();

		internal static void Initialize(string host, ushort port) {
			if (string.IsNullOrEmpty(host) || (port == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(port));
				return;
			}

			if (HttpListener.Prefixes.Count > 0) {
				return;
			}

			switch (host) {
				case "0.0.0.0":
				case "::":
					// Silently map INADDR_ANY to match HttpListener expectations
					host = "*";
					break;
			}

			string url = "http://" + host + ":" + port + "/" + nameof(IPC) + "/";
			HttpListener.Prefixes.Add(url);
		}

		internal static void Start() {
			if (KeepRunning || (HttpListener.Prefixes.Count == 0)) {
				return;
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCStarting, HttpListener.Prefixes.First()));

			try {
				HttpListener.Start();
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			KeepRunning = true;
			Utilities.StartBackgroundFunction(Run);

			ASF.ArchiLogger.LogGenericInfo(Strings.IPCReady);
		}

		internal static void Stop() {
			if (!KeepRunning) {
				return;
			}

			KeepRunning = false;
			HttpListener.Stop();
		}

		private static async Task HandleRequest(HttpListenerContext context) {
			if (context == null) {
				ASF.ArchiLogger.LogNullError(nameof(context));
				return;
			}

			try {
				if (Program.GlobalConfig.SteamOwnerID == 0) {
					ASF.ArchiLogger.LogGenericWarning(Strings.ErrorIPCAccessDenied);
					await context.Response.WriteAsync(HttpStatusCode.Forbidden, Strings.ErrorIPCAccessDenied).ConfigureAwait(false);
					return;
				}

				if (!context.Request.RawUrl.StartsWith("/" + nameof(IPC), StringComparison.Ordinal)) {
					await context.Response.WriteAsync(HttpStatusCode.BadRequest, nameof(HttpStatusCode.BadRequest)).ConfigureAwait(false);
					return;
				}

				switch (context.Request.HttpMethod) {
					case WebRequestMethods.Http.Get:
						for (int i = 0; i < context.Request.QueryString.Count; i++) {
							string key = context.Request.QueryString.GetKey(i);

							switch (key) {
								case "command":
									string command = context.Request.QueryString.Get(i);
									if (string.IsNullOrWhiteSpace(command)) {
										break;
									}

									Bot bot = Bot.Bots.Values.FirstOrDefault();
									if (bot == null) {
										await context.Response.WriteAsync(HttpStatusCode.NotAcceptable, Strings.ErrorNoBotsDefined).ConfigureAwait(false);
										return;
									}

									if (command[0] != '!') {
										command = "!" + command;
									}

									string response = await bot.Response(Program.GlobalConfig.SteamOwnerID, command).ConfigureAwait(false);

									ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCAnswered, command, response));

									await context.Response.WriteAsync(HttpStatusCode.OK, response).ConfigureAwait(false);
									break;
							}
						}

						break;
					default:
						await context.Response.WriteAsync(HttpStatusCode.BadRequest, nameof(HttpStatusCode.BadRequest)).ConfigureAwait(false);
						return;
				}

				if (context.Response.ContentLength64 == 0) {
					await context.Response.WriteAsync(HttpStatusCode.MethodNotAllowed, nameof(HttpStatusCode.MethodNotAllowed)).ConfigureAwait(false);
				}
			} finally {
				context.Response.Close();
			}
		}

		private static async Task Run() {
			while (KeepRunning && HttpListener.IsListening) {
				HttpListenerContext context;

				try {
					context = await HttpListener.GetContextAsync().ConfigureAwait(false);
				} catch (HttpListenerException e) {
					ASF.ArchiLogger.LogGenericException(e);
					continue;
				}

				Utilities.StartBackgroundFunction(() => HandleRequest(context), false);
			}
		}

		private static async Task WriteAsync(this HttpListenerResponse response, HttpStatusCode statusCode, string message) {
			if (string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(message));
				return;
			}

			try {
				if (response.StatusCode != (ushort) statusCode) {
					response.StatusCode = (ushort) statusCode;
				}

				response.AppendHeader("Access-Control-Allow-Origin", "null");

				Encoding encoding = Encoding.UTF8;

				response.ContentEncoding = encoding;
				response.ContentType = "text/plain; charset=" + encoding.WebName;

				byte[] buffer = encoding.GetBytes(message + Environment.NewLine);
				response.ContentLength64 = buffer.Length;

				await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			} catch (Exception e) {
				response.StatusCode = (ushort) HttpStatusCode.InternalServerError;
				ASF.ArchiLogger.LogGenericDebugException(e);
			}
		}
	}
}