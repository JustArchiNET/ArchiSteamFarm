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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using Unosquare.Labs.EmbedIO;
using Unosquare.Labs.EmbedIO.Constants;
using Unosquare.Labs.EmbedIO.Modules;
using Unosquare.Swan;

namespace ArchiSteamFarm {
	[SuppressMessage("ReSharper", "UnusedMember.Global")]
	internal sealed class IPCWebApiController : WebApiController {
		[WebApiHandler(HttpVerbs.Get, "/ipc")]
		public async Task<bool> ExecuteCommandObsolete(WebServer server, HttpListenerContext context) {
			if ((server == null) || (context == null)) {
				ASF.ArchiLogger.LogNullError(nameof(server) + " || " + nameof(context));
				return false;
			}

			string command = context.QueryString("command");
			if (string.IsNullOrEmpty(command)) {
				await context.PlainTextResponse(string.Format(Strings.ErrorIsEmpty, nameof(command)), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			Bot targetBot = Bot.Bots.OrderBy(bot => bot.Key).Select(bot => bot.Value).FirstOrDefault();
			if (targetBot == null) {
				await context.PlainTextResponse(Strings.ErrorNoBotsDefined, HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			if (command[0] != '!') {
				command = "!" + command;
			}

			string content = await targetBot.Response(Program.GlobalConfig.SteamOwnerID, command).ConfigureAwait(false);

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCAnswered, command, content));

			await context.PlainTextResponse(content).ConfigureAwait(false);
			return true;
		}
	}

	internal static class IPC {
		internal static bool IsRunning => WebServerCancellationToken?.IsCancellationRequested == false;

		private static WebServer WebServer;
		private static CancellationTokenSource WebServerCancellationToken;

		internal static void Initialize(string host, ushort port) {
			if (string.IsNullOrEmpty(host) || (port == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(port));
				return;
			}

			if (WebServer != null) {
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

			Terminal.Settings.DisplayLoggingMessageType = LogMessageType.None;

			Terminal.OnLogMessageReceived += OnLogMessageReceived;

			WebServer = new WebServer(url);
			WebServer.RegisterModule(new WebApiModule());
			WebServer.Module<WebApiModule>().RegisterController<IPCWebApiController>();
		}

		internal static async Task<bool> PlainTextResponse(this HttpListenerContext context, string content, HttpStatusCode statusCode = HttpStatusCode.OK) {
			if ((context == null) || string.IsNullOrEmpty(content)) {
				ASF.ArchiLogger.LogNullError(nameof(context) + " || " + nameof(content));
				return false;
			}

			try {
				if (context.Response.StatusCode != (ushort) statusCode) {
					context.Response.StatusCode = (ushort) statusCode;
				}

				context.Response.AppendHeader("Access-Control-Allow-Origin", "null");

				Encoding encoding = Encoding.UTF8;

				context.Response.ContentEncoding = encoding;
				context.Response.ContentType = "text/plain; charset=" + encoding.WebName;

				byte[] buffer = encoding.GetBytes(content + Environment.NewLine);
				context.Response.ContentLength64 = buffer.Length;

				await context.Response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericDebugException(e);
				return false;
			}

			return true;
		}

		internal static void Start() {
			if (IsRunning || (WebServer == null) || (WebServer.UrlPrefixes.Count == 0)) {
				return;
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCStarting, WebServer.UrlPrefixes.First()));

			// Fail early if we're not able to start our listener

			try {
				WebServer.Listener.Start();
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			WebServerCancellationToken = new CancellationTokenSource();
			Utilities.StartBackgroundFunction(() => Run(WebServerCancellationToken));

			ASF.ArchiLogger.LogGenericInfo(Strings.IPCReady);
		}

		internal static void Stop() {
			if (WebServerCancellationToken == null) {
				return;
			}

			Release(WebServerCancellationToken);
		}

		private static void OnLogMessageReceived(object sender, LogMessageReceivedEventArgs e) {
			// Note: it's valid for sender to be null in this function
			if (e == null) {
				ASF.ArchiLogger.LogNullError(nameof(e));
				return;
			}

			if (string.IsNullOrEmpty(e.Message)) {
				return;
			}

			string message = e.Source + " | " + e.Message;

			switch (e.MessageType) {
				case LogMessageType.Error:
				case LogMessageType.Warning:
					ASF.ArchiLogger.LogGenericWarning(message);
					break;
				case LogMessageType.Info:
					ASF.ArchiLogger.LogGenericDebug(message);
					break;
				default:
					ASF.ArchiLogger.LogGenericTrace(message);
					break;
			}
		}

		private static void Release(CancellationTokenSource cts) {
			if (cts == null) {
				ASF.ArchiLogger.LogNullError(nameof(cts));
				return;
			}

			if (cts == WebServerCancellationToken) {
				WebServerCancellationToken = null;
			}

			if (!cts.IsCancellationRequested) {
				cts.Cancel();
			}

			WebServer.Listener.Stop();
		}

		private static async Task Run(CancellationTokenSource cts) {
			if (cts == null) {
				ASF.ArchiLogger.LogNullError(nameof(cts));
				return;
			}

			using (cts) {
				while ((cts == WebServerCancellationToken) && !cts.IsCancellationRequested) {
					try {
						await WebServer.RunAsync(cts.Token).ConfigureAwait(false);
					} catch (Exception e) {
						ASF.ArchiLogger.LogGenericException(e);
						Release(cts);
					}
				}
			}
		}
	}
}