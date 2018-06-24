//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal static class IPC {
		private const byte FailedAuthorizationsCooldown = 1; // In hours
		private const byte MaxFailedAuthorizationAttempts = 5;

		internal static bool IsRunning => IsHandlingRequests || IsListening;

		private static readonly ConcurrentDictionary<WebSocket, SemaphoreSlim> ActiveLogWebSockets = new ConcurrentDictionary<WebSocket, SemaphoreSlim>();
		private static readonly SemaphoreSlim AuthorizationSemaphore = new SemaphoreSlim(1, 1);

		private static readonly HashSet<string> CompressableContentTypes = new HashSet<string> {
			"application/javascript",
			"application/json",
			"text/css",
			"text/html",
			"text/plain"
		};

		private static readonly ConcurrentDictionary<IPAddress, byte> FailedAuthorizations = new ConcurrentDictionary<IPAddress, byte>();

		private static readonly Dictionary<string, string> MimeTypes = new Dictionary<string, string>(8) {
			{ ".css", "text/css" },
			{ ".html", "text/html" },
			{ ".ico", "image/x-icon" },
			{ ".jpg", "image/jpeg" },
			{ ".js", "application/javascript" },
			{ ".json", "application/json" },
			{ ".png", "image/png" },
			{ ".txt", "text/plain" }
		};

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

		private static Timer ClearFailedAuthorizationsTimer;
		private static HistoryTarget HistoryTarget;
		private static HttpListener HttpListener;
		private static bool IsHandlingRequests;

		internal static void OnNewHistoryTarget(HistoryTarget historyTarget) {
			if (historyTarget == null) {
				ASF.ArchiLogger.LogNullError(nameof(historyTarget));
				return;
			}

			if (HistoryTarget != null) {
				HistoryTarget.NewHistoryEntry -= OnNewHistoryEntry;
				HistoryTarget = null;
			}

			historyTarget.NewHistoryEntry += OnNewHistoryEntry;
			HistoryTarget = historyTarget;
		}

		internal static void Start(HashSet<string> prefixes) {
			if ((prefixes == null) || (prefixes.Count == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(prefixes));
				return;
			}

			if (!HttpListener.IsSupported) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, "!HttpListener.IsSupported"));
				return;
			}

			if (IsListening) {
				return;
			}

			HttpListener = new HttpListener { IgnoreWriteExceptions = true };

			try {
				foreach (string prefix in prefixes) {
					ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCStarting, prefix));
					HttpListener.Prefixes.Add(prefix);
				}

				HttpListener.Start();
			} catch (Exception e) {
				// HttpListener can dispose itself on error, so don't keep it around
				HttpListener = null;
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			if (ClearFailedAuthorizationsTimer == null) {
				ClearFailedAuthorizationsTimer = new Timer(
					e => FailedAuthorizations.Clear(),
					null,
					TimeSpan.FromHours(FailedAuthorizationsCooldown), // Delay
					TimeSpan.FromHours(FailedAuthorizationsCooldown) // Period
				);
			}

			Logging.InitHistoryLogger();
			Utilities.InBackground(HandleRequests, true);
			ASF.ArchiLogger.LogGenericInfo(Strings.IPCReady);
		}

		internal static void Stop() {
			if (!HttpListener.IsSupported) {
				ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningFailedWithError, "!HttpListener.IsSupported"));
				return;
			}

			if (!IsListening) {
				return;
			}

			if (ClearFailedAuthorizationsTimer != null) {
				ClearFailedAuthorizationsTimer.Dispose();
				ClearFailedAuthorizationsTimer = null;
			}

			// We must set HttpListener to null before stopping it, so HandleRequests() knows that exception is expected
			HttpListener httpListener = HttpListener;
			HttpListener = null;

			httpListener.Stop();
		}

		private static async Task<bool> HandleApi(HttpListenerContext context, string[] arguments, byte argumentsIndex) {
			if ((context == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(context) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			switch (arguments[argumentsIndex]) {
				case "ASF":
					return await HandleApiASF(context.Request, context.Response, arguments, ++argumentsIndex).ConfigureAwait(false);
				case "Bot/":
					return await HandleApiBot(context.Request, context.Response, arguments, ++argumentsIndex).ConfigureAwait(false);
				case "Command":
				case "Command/":
					return await HandleApiCommand(context.Request, context.Response, arguments, ++argumentsIndex).ConfigureAwait(false);
				case "GamesToRedeemInBackground/":
					return await HandleApiGamesToRedeemInBackground(context.Request, context.Response, arguments, ++argumentsIndex).ConfigureAwait(false);
				case "Log":
					return await HandleApiLog(context, arguments, ++argumentsIndex).ConfigureAwait(false);
				case "Structure/":
					return await HandleApiStructure(context.Request, context.Response, arguments, ++argumentsIndex).ConfigureAwait(false);
				case "Type/":
					return await HandleApiType(context.Request, context.Response, arguments, ++argumentsIndex).ConfigureAwait(false);
				case "WWW/":
					return await HandleApiWWW(context.Request, context.Response, arguments, ++argumentsIndex).ConfigureAwait(false);
				default:
					return false;
			}
		}

		private static async Task<bool> HandleApiASF(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Get:
					return await HandleApiASFGet(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				case HttpMethods.Post:
					return await HandleApiASFPost(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiASFGet(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			uint memoryUsage = (uint) GC.GetTotalMemory(false) / 1024;

			DateTime processStartTime;

			using (Process process = Process.GetCurrentProcess()) {
				processStartTime = process.StartTime;
			}

			ASFResponse asfResponse = new ASFResponse(Program.GlobalConfig, memoryUsage, processStartTime, SharedInfo.Version);

			await ResponseJsonObject(request, response, new GenericResponse<ASFResponse>(true, "OK", asfResponse)).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiASFPost(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			const string requiredContentType = "application/json";

			if (string.IsNullOrEmpty(request.ContentType) || ((request.ContentType != requiredContentType) && !request.ContentType.StartsWith(requiredContentType + ";", StringComparison.Ordinal))) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, nameof(request.ContentType) + " must be declared as " + requiredContentType), HttpStatusCode.NotAcceptable).ConfigureAwait(false);
				return true;
			}

			string body;
			using (StreamReader reader = new StreamReader(request.InputStream)) {
				body = await reader.ReadToEndAsync().ConfigureAwait(false);
			}

			if (string.IsNullOrEmpty(body)) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorIsEmpty, nameof(body))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			ASFRequest jsonRequest;

			try {
				jsonRequest = JsonConvert.DeserializeObject<ASFRequest>(body);
			} catch (Exception e) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorParsingObject, nameof(jsonRequest)) + Environment.NewLine + e), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			if (jsonRequest == null) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorObjectIsNull, nameof(jsonRequest))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			if (jsonRequest.KeepSensitiveDetails) {
				if (string.IsNullOrEmpty(jsonRequest.GlobalConfig.WebProxyPassword) && !string.IsNullOrEmpty(Program.GlobalConfig.WebProxyPassword)) {
					jsonRequest.GlobalConfig.WebProxyPassword = Program.GlobalConfig.WebProxyPassword;
				}
			}

			string filePath = Path.Combine(SharedInfo.ConfigDirectory, SharedInfo.GlobalConfigFileName);

			if (!await GlobalConfig.Write(filePath, jsonRequest.GlobalConfig).ConfigureAwait(false)) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, "Writing global config failed, check ASF log for details"), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			await ResponseJsonObject(request, response, new GenericResponse<object>(true, "OK")).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiBot(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Delete:
					return await HandleApiBotDelete(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				case HttpMethods.Get:
					return await HandleApiBotGet(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				case HttpMethods.Post:
					return await HandleApiBotPost(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiBotDelete(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			string argument = WebUtility.UrlDecode(string.Join("", arguments.Skip(argumentsIndex)));

			HashSet<Bot> bots = Bot.GetBots(argument);
			if ((bots == null) || (bots.Count == 0)) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.BotNotFound, argument)), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			IEnumerable<Task<bool>> tasks = bots.Select(bot => bot.DeleteAllRelatedFiles());
			ICollection<bool> results;

			switch (Program.GlobalConfig.OptimizationMode) {
				case GlobalConfig.EOptimizationMode.MinMemoryUsage:
					results = new List<bool>(bots.Count);
					foreach (Task<bool> task in tasks) {
						results.Add(await task.ConfigureAwait(false));
					}

					break;
				default:
					results = await Task.WhenAll(tasks).ConfigureAwait(false);
					break;
			}

			if (results.Any(result => !result)) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, "Removing one or more files failed, check ASF log for details"), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			await ResponseJsonObject(request, response, new GenericResponse<object>(true, "OK")).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiBotGet(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			string argument = WebUtility.UrlDecode(string.Join("", arguments.Skip(argumentsIndex)));

			HashSet<Bot> bots = Bot.GetBots(argument);
			if ((bots == null) || (bots.Count == 0)) {
				await ResponseJsonObject(request, response, new GenericResponse<HashSet<Bot>>(false, string.Format(Strings.BotNotFound, argument)), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			await ResponseJsonObject(request, response, new GenericResponse<HashSet<Bot>>(true, "OK", bots)).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiBotPost(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			const string requiredContentType = "application/json";

			if (string.IsNullOrEmpty(request.ContentType) || ((request.ContentType != requiredContentType) && !request.ContentType.StartsWith(requiredContentType + ";", StringComparison.Ordinal))) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, nameof(request.ContentType) + " must be declared as " + requiredContentType), HttpStatusCode.NotAcceptable).ConfigureAwait(false);
				return true;
			}

			string body;
			using (StreamReader reader = new StreamReader(request.InputStream)) {
				body = await reader.ReadToEndAsync().ConfigureAwait(false);
			}

			if (string.IsNullOrEmpty(body)) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorIsEmpty, nameof(body))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			BotRequest jsonRequest;

			try {
				jsonRequest = JsonConvert.DeserializeObject<BotRequest>(body);
			} catch (Exception e) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorParsingObject, nameof(jsonRequest)) + Environment.NewLine + e), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			if (jsonRequest == null) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorObjectIsNull, nameof(jsonRequest))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			string botName = WebUtility.UrlDecode(arguments[argumentsIndex]);

			if (jsonRequest.KeepSensitiveDetails && Bot.Bots.TryGetValue(botName, out Bot bot)) {
				if (string.IsNullOrEmpty(jsonRequest.BotConfig.SteamLogin) && !string.IsNullOrEmpty(bot.BotConfig.SteamLogin)) {
					jsonRequest.BotConfig.SteamLogin = bot.BotConfig.SteamLogin;
				}

				if (string.IsNullOrEmpty(jsonRequest.BotConfig.SteamParentalPIN) && !string.IsNullOrEmpty(bot.BotConfig.SteamParentalPIN)) {
					jsonRequest.BotConfig.SteamParentalPIN = bot.BotConfig.SteamParentalPIN;
				}

				if (string.IsNullOrEmpty(jsonRequest.BotConfig.SteamPassword) && !string.IsNullOrEmpty(bot.BotConfig.SteamPassword)) {
					jsonRequest.BotConfig.SteamPassword = bot.BotConfig.SteamPassword;
				}
			}

			string filePath = Path.Combine(SharedInfo.ConfigDirectory, botName + SharedInfo.ConfigExtension);

			if (!await BotConfig.Write(filePath, jsonRequest.BotConfig).ConfigureAwait(false)) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, "Writing bot config failed, check ASF log for details"), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			await ResponseJsonObject(request, response, new GenericResponse<object>(true, "OK")).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiCommand(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (Program.GlobalConfig.SteamOwnerID == 0) {
				await ResponseJsonObject(request, response, new GenericResponse<string>(false, string.Format(Strings.ErrorIsEmpty, nameof(Program.GlobalConfig.SteamOwnerID))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Post:
					return await HandleApiCommandPost(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiCommandPost(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			string argument = WebUtility.UrlDecode(string.Join("", arguments.Skip(argumentsIndex)));
			if (string.IsNullOrEmpty(argument)) {
				await ResponseJsonObject(request, response, new GenericResponse<string>(false, string.Format(Strings.ErrorIsEmpty, nameof(argument))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			Bot targetBot = Bot.Bots.OrderBy(bot => bot.Key).Select(bot => bot.Value).FirstOrDefault();
			if (targetBot == null) {
				await ResponseJsonObject(request, response, new GenericResponse<string>(false, Strings.ErrorNoBotsDefined), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			if (!string.IsNullOrEmpty(Program.GlobalConfig.CommandPrefix) && !argument.StartsWith(Program.GlobalConfig.CommandPrefix, StringComparison.Ordinal)) {
				argument = Program.GlobalConfig.CommandPrefix + argument;
			}

			string content = await targetBot.Response(Program.GlobalConfig.SteamOwnerID, argument).ConfigureAwait(false);

			await ResponseJsonObject(request, response, new GenericResponse<string>(true, "OK", content)).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiGamesToRedeemInBackground(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Post:
					return await HandleApiGamesToRedeemInBackgroundPost(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiGamesToRedeemInBackgroundPost(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			string argument = WebUtility.UrlDecode(string.Join("", arguments.Skip(argumentsIndex)));

			if (!Bot.Bots.TryGetValue(argument, out Bot bot)) {
				await ResponseJsonObject(request, response, new GenericResponse<OrderedDictionary>(false, string.Format(Strings.BotNotFound, argument)), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			const string requiredContentType = "application/json";

			if (string.IsNullOrEmpty(request.ContentType) || ((request.ContentType != requiredContentType) && !request.ContentType.StartsWith(requiredContentType + ";", StringComparison.Ordinal))) {
				await ResponseJsonObject(request, response, new GenericResponse<OrderedDictionary>(false, nameof(request.ContentType) + " must be declared as " + requiredContentType), HttpStatusCode.NotAcceptable).ConfigureAwait(false);
				return true;
			}

			string body;
			using (StreamReader reader = new StreamReader(request.InputStream)) {
				body = await reader.ReadToEndAsync().ConfigureAwait(false);
			}

			if (string.IsNullOrEmpty(body)) {
				await ResponseJsonObject(request, response, new GenericResponse<OrderedDictionary>(false, string.Format(Strings.ErrorIsEmpty, nameof(body))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			GamesToRedeemInBackgroundRequest jsonRequest;

			try {
				jsonRequest = JsonConvert.DeserializeObject<GamesToRedeemInBackgroundRequest>(body);
			} catch (Exception e) {
				await ResponseJsonObject(request, response, new GenericResponse<OrderedDictionary>(false, string.Format(Strings.ErrorParsingObject, nameof(jsonRequest)) + Environment.NewLine + e), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			if (jsonRequest == null) {
				await ResponseJsonObject(request, response, new GenericResponse<OrderedDictionary>(false, string.Format(Strings.ErrorObjectIsNull, nameof(jsonRequest))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			if (jsonRequest.GamesToRedeemInBackground.Count == 0) {
				await ResponseJsonObject(request, response, new GenericResponse<OrderedDictionary>(false, string.Format(Strings.ErrorIsEmpty, nameof(jsonRequest.GamesToRedeemInBackground))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			await bot.ValidateAndAddGamesToRedeemInBackground(jsonRequest.GamesToRedeemInBackground).ConfigureAwait(false);

			await ResponseJsonObject(request, response, new GenericResponse<OrderedDictionary>(true, "OK", jsonRequest.GamesToRedeemInBackground)).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiLog(HttpListenerContext context, string[] arguments, byte argumentsIndex) {
			if ((context == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(context) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			switch (context.Request.HttpMethod) {
				case HttpMethods.Get:
					return await HandleApiLogGet(context, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(context.Request, context.Response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiLogGet(HttpListenerContext context, string[] arguments, byte argumentsIndex) {
			if ((context == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(context) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (!context.Request.IsWebSocketRequest) {
				await ResponseStatusCode(context.Request, context.Response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
				return true;
			}

			try {
				HttpListenerWebSocketContext webSocketContext = await context.AcceptWebSocketAsync(null).ConfigureAwait(false);

				SemaphoreSlim sendSemaphore = new SemaphoreSlim(1, 1);
				if (!ActiveLogWebSockets.TryAdd(webSocketContext.WebSocket, sendSemaphore)) {
					sendSemaphore.Dispose();
					return true;
				}

				try {
					// Push initial history if available
					if (HistoryTarget != null) {
						await Task.WhenAll(HistoryTarget.ArchivedMessages.Select(archivedMessage => PostLoggedMessageUpdate(webSocketContext.WebSocket, sendSemaphore, archivedMessage))).ConfigureAwait(false);
					}

					while (webSocketContext.WebSocket.State == WebSocketState.Open) {
						WebSocketReceiveResult result = await webSocketContext.WebSocket.ReceiveAsync(new byte[0], CancellationToken.None).ConfigureAwait(false);

						if (result.MessageType != WebSocketMessageType.Close) {
							await webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.InvalidMessageType, "You're not supposed to be sending any message but Close!", CancellationToken.None).ConfigureAwait(false);
							break;
						}

						await webSocketContext.WebSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).ConfigureAwait(false);
						break;
					}
				} finally {
					if (ActiveLogWebSockets.TryRemove(webSocketContext.WebSocket, out SemaphoreSlim closedSemaphore)) {
						await closedSemaphore.WaitAsync().ConfigureAwait(false); // Ensure that our semaphore is truly closed by now
						closedSemaphore.Dispose();
					}
				}

				return true;
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
				return true;
			} catch (WebSocketException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
				return true;
			}
		}

		private static async Task<bool> HandleApiStructure(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Get:
					return await HandleApiStructureGet(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiStructureGet(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			string argument = WebUtility.UrlDecode(string.Join("", arguments.Skip(argumentsIndex)));
			Type targetType = Type.GetType(argument);

			if (targetType == null) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorIsInvalid, nameof(argument))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			object obj;

			try {
				obj = Activator.CreateInstance(targetType, true);
			} catch (Exception e) {
				await ResponseJsonObject(request, response, new GenericResponse<object>(false, string.Format(Strings.ErrorParsingObject, targetType) + Environment.NewLine + e), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			await ResponseJsonObject(request, response, new GenericResponse<object>(true, "OK", obj)).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiType(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Get:
					return await HandleApiTypeGet(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiTypeGet(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			string argument = WebUtility.UrlDecode(string.Join("", arguments.Skip(argumentsIndex)));
			Type targetType = Type.GetType(argument);

			if (targetType == null) {
				await ResponseJsonObject(request, response, new GenericResponse<TypeResponse>(false, string.Format(Strings.ErrorIsInvalid, nameof(argument))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			string baseType = targetType.BaseType?.GetUnifiedName();
			HashSet<string> customAttributes = targetType.CustomAttributes.Select(attribute => attribute.AttributeType.GetUnifiedName()).ToHashSet();
			string underlyingType = null;

			Dictionary<string, string> body = new Dictionary<string, string>();

			if (targetType.IsClass) {
				foreach (FieldInfo field in targetType.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(field => !field.IsPrivate)) {
					body[field.Name] = field.FieldType.GetUnifiedName();
				}

				foreach (PropertyInfo property in targetType.GetProperties(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public).Where(property => property.CanRead && !property.GetMethod.IsPrivate)) {
					body[property.Name] = property.PropertyType.GetUnifiedName();
				}
			} else if (targetType.IsEnum) {
				Type enumType = Enum.GetUnderlyingType(targetType);
				underlyingType = enumType.GetUnifiedName();

				foreach (object value in Enum.GetValues(targetType)) {
					body[value.ToString()] = Convert.ChangeType(value, enumType).ToString();
				}
			}

			TypeResponse.TypeProperties properties = new TypeResponse.TypeProperties(baseType, customAttributes.Count > 0 ? customAttributes : null, underlyingType);

			await ResponseJsonObject(request, response, new GenericResponse<TypeResponse>(true, "OK", new TypeResponse(body, properties))).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleApiWWW(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			switch (arguments[argumentsIndex]) {
				case "Directory/":
					return await HandleApiWWWDirectory(request, response, arguments, ++argumentsIndex).ConfigureAwait(false);
				default:
					return false;
			}
		}

		private static async Task<bool> HandleApiWWWDirectory(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Get:
					return await HandleApiWWWDirectoryGet(request, response, arguments, argumentsIndex).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleApiWWWDirectoryGet(HttpListenerRequest request, HttpListenerResponse response, string[] arguments, byte argumentsIndex) {
			if ((request == null) || (response == null) || (arguments == null) || (argumentsIndex == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(arguments) + " || " + nameof(argumentsIndex));
				return false;
			}

			if (arguments.Length <= argumentsIndex) {
				return false;
			}

			string argument = WebUtility.UrlDecode(string.Join("", arguments.Skip(argumentsIndex)));

			string directory = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.WebsiteDirectory, argument);
			if (!Directory.Exists(directory)) {
				await ResponseJsonObject(request, response, new GenericResponse<HashSet<string>>(false, string.Format(Strings.ErrorIsInvalid, nameof(directory))), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			string[] files;

			try {
				files = Directory.GetFiles(directory);
			} catch (Exception e) {
				await ResponseJsonObject(request, response, new GenericResponse<HashSet<string>>(false, string.Format(Strings.ErrorParsingObject, nameof(files)) + Environment.NewLine + e), HttpStatusCode.BadRequest).ConfigureAwait(false);
				return true;
			}

			HashSet<string> result = files.Select(Path.GetFileName).ToHashSet();

			await ResponseJsonObject(request, response, new GenericResponse<HashSet<string>>(true, "OK", result)).ConfigureAwait(false);
			return true;
		}

		private static async Task<bool> HandleFile(HttpListenerRequest request, HttpListenerResponse response, string absolutePath) {
			if ((request == null) || (response == null) || string.IsNullOrEmpty(absolutePath)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(absolutePath));
				return false;
			}

			switch (request.HttpMethod) {
				case HttpMethods.Get:
					return await HandleFileGet(request, response, absolutePath).ConfigureAwait(false);
				default:
					await ResponseStatusCode(request, response, HttpStatusCode.MethodNotAllowed).ConfigureAwait(false);
					return true;
			}
		}

		private static async Task<bool> HandleFileGet(HttpListenerRequest request, HttpListenerResponse response, string absolutePath) {
			if ((request == null) || (response == null) || string.IsNullOrEmpty(absolutePath)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(absolutePath));
				return false;
			}

			string filePath = Path.Combine(SharedInfo.HomeDirectory, SharedInfo.WebsiteDirectory) + Path.DirectorySeparatorChar + absolutePath.Replace('/', Path.DirectorySeparatorChar);
			if (Directory.Exists(filePath)) {
				filePath = Path.Combine(filePath, "index.html");
			}

			if (!File.Exists(filePath)) {
				return false;
			}

			await ResponseFile(request, response, filePath).ConfigureAwait(false);
			return true;
		}

		private static async Task HandleRequest(HttpListenerContext context) {
			if (context == null) {
				ASF.ArchiLogger.LogNullError(nameof(context));
				return;
			}

			try {
				bool handled;

				if ((context.Request.Url.Segments.Length >= 2) && context.Request.Url.Segments[1].Equals("Api/")) {
					if (!await IsAuthorized(context).ConfigureAwait(false)) {
						return;
					}

					handled = await HandleApi(context, context.Request.Url.Segments, 2).ConfigureAwait(false);
				} else {
					handled = await HandleFile(context.Request, context.Response, context.Request.Url.AbsolutePath).ConfigureAwait(false);
				}

				if (!handled) {
					await ResponseStatusCode(context.Request, context.Response, HttpStatusCode.NotFound).ConfigureAwait(false);
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

					Utilities.InBackground(() => HandleRequest(context));
				}
			} catch (ObjectDisposedException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} finally {
				IsHandlingRequests = false;
			}
		}

		private static async Task<bool> IsAuthorized(HttpListenerContext context) {
			if (string.IsNullOrEmpty(Program.GlobalConfig.IPCPassword)) {
				return true;
			}

			IPAddress ipAddress = context.Request.RemoteEndPoint?.Address;

			bool authorized;

			if (ipAddress != null) {
				if (FailedAuthorizations.TryGetValue(ipAddress, out byte attempts)) {
					if (attempts >= MaxFailedAuthorizationAttempts) {
						await ResponseStatusCode(context.Request, context.Response, HttpStatusCode.Forbidden).ConfigureAwait(false);
						return false;
					}
				}

				await AuthorizationSemaphore.WaitAsync().ConfigureAwait(false);

				try {
					if (FailedAuthorizations.TryGetValue(ipAddress, out attempts)) {
						if (attempts >= MaxFailedAuthorizationAttempts) {
							await ResponseStatusCode(context.Request, context.Response, HttpStatusCode.Forbidden).ConfigureAwait(false);
							return false;
						}
					}

					string password = context.Request.Headers.Get("Authentication");
					if (string.IsNullOrEmpty(password)) {
						password = context.Request.QueryString.Get("password");
					}

					authorized = password == Program.GlobalConfig.IPCPassword;

					if (authorized) {
						FailedAuthorizations.TryRemove(ipAddress, out _);
					} else {
						FailedAuthorizations[ipAddress] = FailedAuthorizations.TryGetValue(ipAddress, out attempts) ? ++attempts : (byte) 1;
					}
				} finally {
					AuthorizationSemaphore.Release();
				}
			} else {
				string password = context.Request.Headers.Get("Authentication");
				if (string.IsNullOrEmpty(password)) {
					password = context.Request.QueryString.Get("password");
				}

				authorized = password == Program.GlobalConfig.IPCPassword;
			}

			if (authorized) {
				return true;
			}

			await ResponseStatusCode(context.Request, context.Response, HttpStatusCode.Unauthorized).ConfigureAwait(false);
			return false;
		}

		private static async void OnNewHistoryEntry(object sender, HistoryTarget.NewHistoryEntryArgs newHistoryEntryArgs) {
			if ((sender == null) || (newHistoryEntryArgs == null)) {
				ASF.ArchiLogger.LogNullError(nameof(sender) + " || " + nameof(newHistoryEntryArgs));
				return;
			}

			if (ActiveLogWebSockets.Count == 0) {
				return;
			}

			string json = JsonConvert.SerializeObject(new GenericResponse<string>(true, "OK", newHistoryEntryArgs.Message));
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
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
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

			string response = JsonConvert.SerializeObject(new GenericResponse<string>(true, "OK", loggedMessage));
			await PostLoggedJsonUpdate(webSocket, sendSemaphore, response).ConfigureAwait(false);
		}

		private static async Task ResponseBase(HttpListenerRequest request, HttpListenerResponse response, byte[] content, HttpStatusCode statusCode = HttpStatusCode.OK) {
			if ((request == null) || (response == null) || (content == null) || (content.Length == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(content));
				return;
			}

			try {
				if (response.StatusCode != (ushort) statusCode) {
					response.StatusCode = (ushort) statusCode;
				}

				response.AddHeader("Access-Control-Allow-Origin", "*");
				response.AddHeader("Date", DateTime.UtcNow.ToString("R"));

				if (CompressableContentTypes.Contains(response.ContentType)) {
					string acceptEncoding = request.Headers["Accept-Encoding"];

					if (!string.IsNullOrEmpty(acceptEncoding)) {
						if (acceptEncoding.Contains("gzip")) {
							response.AddHeader("Content-Encoding", "gzip");
							using (MemoryStream ms = new MemoryStream()) {
								using (GZipStream stream = new GZipStream(ms, CompressionMode.Compress)) {
									await stream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
								}

								content = ms.ToArray();
							}
						} else if (acceptEncoding.Contains("deflate")) {
							response.AddHeader("Content-Encoding", "deflate");
							using (MemoryStream ms = new MemoryStream()) {
								using (DeflateStream stream = new DeflateStream(ms, CompressionMode.Compress)) {
									await stream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
								}

								content = ms.ToArray();
							}
						}
					}
				}

				response.ContentLength64 = content.Length;
				await response.OutputStream.WriteAsync(content, 0, content.Length).ConfigureAwait(false);
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (ObjectDisposedException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				await ResponseStatusCode(request, response, HttpStatusCode.ServiceUnavailable).ConfigureAwait(false);
			}
		}

		private static async Task ResponseFile(HttpListenerRequest request, HttpListenerResponse response, string filePath) {
			if ((request == null) || (response == null) || string.IsNullOrEmpty(filePath)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(filePath));
				return;
			}

			try {
				response.ContentType = MimeTypes.TryGetValue(Path.GetExtension(filePath), out string mimeType) ? mimeType : "application/octet-stream";

				byte[] content = await RuntimeCompatibility.File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
				await ResponseBase(request, response, content).ConfigureAwait(false);
			} catch (FileNotFoundException) {
				await ResponseStatusCode(request, response, HttpStatusCode.NotFound).ConfigureAwait(false);
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (ObjectDisposedException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				await ResponseStatusCode(request, response, HttpStatusCode.ServiceUnavailable).ConfigureAwait(false);
			}
		}

		private static async Task ResponseJson(HttpListenerRequest request, HttpListenerResponse response, string json, HttpStatusCode statusCode = HttpStatusCode.OK) {
			if ((request == null) || (response == null) || string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(json));
				return;
			}

			await ResponseString(request, response, json, "application/json", statusCode).ConfigureAwait(false);
		}

		private static async Task ResponseJsonObject(HttpListenerRequest request, HttpListenerResponse response, object obj, HttpStatusCode statusCode = HttpStatusCode.OK) {
			if ((request == null) || (response == null) || (obj == null)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(obj));
				return;
			}

			await ResponseJson(request, response, JsonConvert.SerializeObject(obj), statusCode).ConfigureAwait(false);
		}

		private static async Task ResponseStatusCode(HttpListenerRequest request, HttpListenerResponse response, HttpStatusCode statusCode) {
			if ((request == null) || (response == null)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response));
				return;
			}

			string text = (ushort) statusCode + " - " + statusCode;
			await ResponseText(request, response, text, statusCode).ConfigureAwait(false);
		}

		private static async Task ResponseString(HttpListenerRequest request, HttpListenerResponse response, string text, string textType, HttpStatusCode statusCode) {
			if ((request == null) || (response == null) || string.IsNullOrEmpty(text) || string.IsNullOrEmpty(textType)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(text) + " || " + nameof(textType));
				return;
			}

			try {
				if (response.ContentEncoding == null) {
					response.ContentEncoding = Encoding.UTF8;
				}

				response.ContentType = textType + "; charset=" + response.ContentEncoding.WebName;

				byte[] content = response.ContentEncoding.GetBytes(text + Environment.NewLine);
				await ResponseBase(request, response, content, statusCode).ConfigureAwait(false);
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (ObjectDisposedException e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				await ResponseStatusCode(request, response, HttpStatusCode.ServiceUnavailable).ConfigureAwait(false);
			}
		}

		private static async Task ResponseText(HttpListenerRequest request, HttpListenerResponse response, string text, HttpStatusCode statusCode = HttpStatusCode.OK) {
			if ((request == null) || (response == null) || string.IsNullOrEmpty(text)) {
				ASF.ArchiLogger.LogNullError(nameof(request) + " || " + nameof(response) + " || " + nameof(text));
				return;
			}

			await ResponseString(request, response, text, "text/plain", statusCode).ConfigureAwait(false);
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		private sealed class ASFRequest {
#pragma warning disable 649
			[JsonProperty(Required = Required.Always)]
			internal readonly GlobalConfig GlobalConfig;
#pragma warning restore 649

			[JsonProperty(Required = Required.DisallowNull)]
			internal readonly bool KeepSensitiveDetails = true;

			// Deserialized from JSON
			private ASFRequest() { }
		}

		private sealed class ASFResponse {
			[JsonProperty]
			private readonly GlobalConfig GlobalConfig;

			[JsonProperty]
			private readonly uint MemoryUsage;

			[JsonProperty]
			private readonly DateTime ProcessStartTime;

			[JsonProperty]
			private readonly Version Version;

			internal ASFResponse(GlobalConfig globalConfig, uint memoryUsage, DateTime processStartTime, Version version) {
				if ((globalConfig == null) || (memoryUsage == 0) || (processStartTime == DateTime.MinValue) || (version == null)) {
					throw new ArgumentNullException(nameof(memoryUsage) + " || " + nameof(processStartTime) + " || " + nameof(version));
				}

				GlobalConfig = globalConfig;
				MemoryUsage = memoryUsage;
				ProcessStartTime = processStartTime;
				Version = version;
			}
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		private sealed class BotRequest {
#pragma warning disable 649
			[JsonProperty(Required = Required.Always)]
			internal readonly BotConfig BotConfig;
#pragma warning restore 649

			[JsonProperty(Required = Required.DisallowNull)]
			internal readonly bool KeepSensitiveDetails = true;

			// Deserialized from JSON
			private BotRequest() { }
		}

		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		private sealed class GamesToRedeemInBackgroundRequest {
#pragma warning disable 649
			[JsonProperty(Required = Required.Always)]
			internal readonly OrderedDictionary GamesToRedeemInBackground;
#pragma warning restore 649

			// Deserialized from JSON
			private GamesToRedeemInBackgroundRequest() { }
		}

		private sealed class GenericResponse<T> where T : class {
			[JsonProperty]
			private readonly string Message;

			[JsonProperty]
			private readonly T Result;

			[JsonProperty]
			private readonly bool Success;

			internal GenericResponse(bool success, string message = null, T result = null) {
				Success = success;
				Message = message;
				Result = result;
			}
		}

		private static class HttpMethods {
			internal const string Delete = "DELETE";
			internal const string Get = "GET";
			internal const string Post = "POST";
		}

		private sealed class TypeResponse {
			[JsonProperty]
			private readonly Dictionary<string, string> Body;

			[JsonProperty]
			private readonly TypeProperties Properties;

			internal TypeResponse(Dictionary<string, string> body, TypeProperties properties) {
				if ((body == null) || (properties == null)) {
					throw new ArgumentNullException(nameof(body) + " || " + nameof(properties));
				}

				Body = body;
				Properties = properties;
			}

			internal sealed class TypeProperties {
				[JsonProperty]
				private readonly string BaseType;

				[JsonProperty]
				private readonly HashSet<string> CustomAttributes;

				[JsonProperty]
				private readonly string UnderlyingType;

				internal TypeProperties(string baseType = null, HashSet<string> customAttributes = null, string underlyingType = null) {
					BaseType = baseType;
					CustomAttributes = customAttributes;
					UnderlyingType = underlyingType;
				}
			}
		}
	}
}