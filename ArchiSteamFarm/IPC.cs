//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class IPC {
		internal static bool IsRunning => IsHandlingRequests || IsListening;

		private static bool IsListening {
			get {
				try {
					return HttpListener?.IsListening == true;
				} catch (ObjectDisposedException) {
					// HttpListener can dispose itself on error
					return false;
				}
			}
		}

		private static HttpListener HttpListener;
		private static bool IsHandlingRequests;

		internal static void Start(string host, ushort port) {
			if (string.IsNullOrEmpty(host) || (port == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(port));
				return;
			}

			if (!HttpListener.IsSupported) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, "!HttpListener.IsSupported"));
				return;
			}

			switch (host) {
				case "0.0.0.0":
				case "::":
					// Silently map INADDR_ANY to match HttpListener expectations
					host = "*";
					break;
			}

			string url = "http://" + host + ":" + port + "/";
			HttpListener = new HttpListener { IgnoreWriteExceptions = true };

			try {
				ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCStarting, url));

				HttpListener.Prefixes.Add(url);
				HttpListener.Start();
			} catch (Exception e) {
				// HttpListener can dispose itself on error, so don't keep it around
				HttpListener = null;
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			Utilities.StartBackgroundFunction(HandleRequests);
			ASF.ArchiLogger.LogGenericInfo(Strings.IPCReady);
		}

		internal static void Stop() {
			if (!IsListening) {
				return;
			}

			// We must set HttpListener to null before stopping it, so HandleRequests() knows that exception is expected
			HttpListener httpListener = HttpListener;
			HttpListener = null;

			httpListener.Stop();
		}

		private static async Task BaseResponse(this HttpListenerContext context, byte[] response, HttpStatusCode statusCode = HttpStatusCode.OK) {
			if ((context == null) || (response == null) || (response.Length == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(context) + " || " + nameof(response));
				return;
			}

			try {
				if (context.Response.StatusCode != (ushort) statusCode) {
					context.Response.StatusCode = (ushort) statusCode;
				}

				context.Response.AppendHeader("Access-Control-Allow-Origin", "*");

				string acceptEncoding = context.Request.Headers["Accept-Encoding"];

				if (!string.IsNullOrEmpty(acceptEncoding)) {
					if (acceptEncoding.Contains("gzip")) {
						context.Response.AddHeader("Content-Encoding", "gzip");
						using (MemoryStream ms = new MemoryStream()) {
							using (GZipStream stream = new GZipStream(ms, CompressionMode.Compress)) {
								await stream.WriteAsync(response, 0, response.Length).ConfigureAwait(false);
							}

							response = ms.ToArray();
						}
					} else if (acceptEncoding.Contains("deflate")) {
						context.Response.AddHeader("Content-Encoding", "deflate");
						using (MemoryStream ms = new MemoryStream()) {
							using (DeflateStream stream = new DeflateStream(ms, CompressionMode.Compress)) {
								await stream.WriteAsync(response, 0, response.Length).ConfigureAwait(false);
							}

							response = ms.ToArray();
						}
					}
				}

				context.Response.ContentLength64 = response.Length;
				await context.Response.OutputStream.WriteAsync(response, 0, response.Length).ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			}
		}

		private static async Task ExecuteCommand(HttpListenerContext context) {
			if (context == null) {
				ASF.ArchiLogger.LogNullError(nameof(context));
				return;
			}

			string command = context.Request.GetQueryStringValue("command");
			if (string.IsNullOrEmpty(command)) {
				await context.StringResponse(string.Format(Strings.ErrorIsEmpty, nameof(command)), statusCode: HttpStatusCode.BadRequest).ConfigureAwait(false);
				return;
			}

			Bot targetBot = Bot.Bots.OrderBy(bot => bot.Key).Select(bot => bot.Value).FirstOrDefault();
			if (targetBot == null) {
				await context.StringResponse(Strings.ErrorNoBotsDefined, statusCode: HttpStatusCode.BadRequest).ConfigureAwait(false);
				return;
			}

			if (command[0] != '!') {
				command = "!" + command;
			}

			string content = await targetBot.Response(Program.GlobalConfig.SteamOwnerID, command).ConfigureAwait(false);

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCAnswered, command, content));

			await context.StringResponse(content).ConfigureAwait(false);
		}

		private static string GetQueryStringValue(this HttpListenerRequest request, string requestKey) {
			if ((request == null) || string.IsNullOrEmpty(requestKey)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(requestKey));
				return null;
			}

			string result = (from string key in request.QueryString where requestKey.Equals(key) select request.QueryString[key]).FirstOrDefault();
			return result;
		}

		private static async Task HandleGetRequest(HttpListenerContext context) {
			if (context == null) {
				ASF.ArchiLogger.LogNullError(nameof(context));
				return;
			}

			switch (context.Request.Url.LocalPath.ToUpperInvariant()) {
				case "/IPC":
					await ExecuteCommand(context).ConfigureAwait(false);
					break;
			}
		}

		private static async Task HandleRequest(HttpListenerContext context) {
			if (context == null) {
				ASF.ArchiLogger.LogNullError(nameof(context));
				return;
			}

			try {
				if (Program.GlobalConfig.SteamOwnerID == 0) {
					await context.StatusCodeResponse(HttpStatusCode.Forbidden).ConfigureAwait(false);
					return;
				}

				if (!string.IsNullOrEmpty(Program.GlobalConfig.IPCPassword) && (context.Request.GetQueryStringValue("password") != Program.GlobalConfig.IPCPassword)) {
					await context.StatusCodeResponse(HttpStatusCode.Unauthorized).ConfigureAwait(false);
					return;
				}

				switch (context.Request.HttpMethod) {
					case WebRequestMethods.Http.Get:
						await HandleGetRequest(context).ConfigureAwait(false);
						break;
				}

				if (context.Response.ContentLength64 == 0) {
					await context.StatusCodeResponse(HttpStatusCode.NotFound);
				}
			} finally {
				context.Response.Close();
			}
		}

		private static async Task HandleRequests() {
			if (IsHandlingRequests) {
				return;
			}

			IsHandlingRequests = true;

			try {
				while (IsListening) {
					Task<HttpListenerContext> task = HttpListener?.GetContextAsync();
					if (task == null) {
						return;
					}

					HttpListenerContext context;

					try {
						context = await task.ConfigureAwait(false);
					} catch (HttpListenerException e) {
						// If HttpListener is null then we're stopping HttpListener, so this exception is expected, ignore it
						if (HttpListener == null) {
							return;
						}

						// Otherwise this is an error, and HttpListener can dispose itself in this situation, so don't keep it around
						HttpListener = null;
						ASF.ArchiLogger.LogGenericException(e);
						return;
					}

					Utilities.StartBackgroundFunction(() => HandleRequest(context), false);
				}
			} finally {
				IsHandlingRequests = false;
			}
		}

		private static async Task StatusCodeResponse(this HttpListenerContext context, HttpStatusCode statusCode) {
			if (context == null) {
				ASF.ArchiLogger.LogNullError(nameof(context));
				return;
			}

			string content = (ushort) statusCode + " - " + statusCode;
			await context.StringResponse(content, statusCode: statusCode).ConfigureAwait(false);
		}

		private static async Task StringResponse(this HttpListenerContext context, string content, string textType = "text/plain", HttpStatusCode statusCode = HttpStatusCode.OK) {
			if ((context == null) || string.IsNullOrEmpty(content) || string.IsNullOrEmpty(textType)) {
				ASF.ArchiLogger.LogNullError(nameof(context) + " || " + nameof(content) + " || " + nameof(textType));
				return;
			}

			if (context.Response.ContentEncoding == null) {
				context.Response.ContentEncoding = Encoding.UTF8;
			}

			context.Response.ContentType = textType + "; charset=" + context.Response.ContentEncoding.WebName;

			byte[] response = context.Response.ContentEncoding.GetBytes(content + Environment.NewLine);
			await BaseResponse(context, response, statusCode).ConfigureAwait(false);
		}
	}
}