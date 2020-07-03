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
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using AngleSharp;
using AngleSharp.Dom;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using JetBrains.Annotations;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	public sealed class WebBrowser : IDisposable {
		[PublicAPI]
		public const byte MaxTries = 5; // Defines maximum number of recommended tries for a single request

		internal const byte MaxConnections = 5; // Defines maximum number of connections per ServicePoint. Be careful, as it also defines maximum number of sockets in CLOSE_WAIT state

		private const byte ExtendedTimeoutMultiplier = 10; // Defines multiplier of timeout for WebBrowsers dealing with huge data (ASF update)
		private const byte MaxIdleTime = 15; // Defines in seconds, how long socket is allowed to stay in CLOSE_WAIT state after there are no connections to it

		[PublicAPI]
		public TimeSpan Timeout => HttpClient.Timeout;

		internal readonly CookieContainer CookieContainer = new CookieContainer();

		private readonly ArchiLogger ArchiLogger;
		private readonly HttpClient HttpClient;
		private readonly HttpClientHandler HttpClientHandler;

		internal WebBrowser([NotNull] ArchiLogger archiLogger, IWebProxy webProxy = null, bool extendedTimeout = false) {
			ArchiLogger = archiLogger ?? throw new ArgumentNullException(nameof(archiLogger));

			HttpClientHandler = new HttpClientHandler {
				AllowAutoRedirect = false, // This must be false if we want to handle custom redirection schemes such as "steammobile://"

#if !NETFRAMEWORK
				AutomaticDecompression = DecompressionMethods.All,
#else
				AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
#endif

				CookieContainer = CookieContainer
			};

			if (webProxy != null) {
				HttpClientHandler.Proxy = webProxy;
				HttpClientHandler.UseProxy = true;
			}

			if (!RuntimeCompatibility.IsRunningOnMono) {
				HttpClientHandler.MaxConnectionsPerServer = MaxConnections;
			}

			HttpClient = GenerateDisposableHttpClient(extendedTimeout);
		}

		public void Dispose() {
			HttpClient.Dispose();
			HttpClientHandler.Dispose();
		}

		[NotNull]
		[PublicAPI]
		public HttpClient GenerateDisposableHttpClient(bool extendedTimeout = false) {
			HttpClient result = new HttpClient(HttpClientHandler, false) {
#if !NETFRAMEWORK
				DefaultRequestVersion = HttpVersion.Version20,
#endif
				Timeout = TimeSpan.FromSeconds(extendedTimeout ? ExtendedTimeoutMultiplier * ASF.GlobalConfig.ConnectionTimeout : ASF.GlobalConfig.ConnectionTimeout)
			};

			// Most web services expect that UserAgent is set, so we declare it globally
			// If you by any chance came here with a very "clever" idea of hiding your ass by changing default ASF user-agent then here is a very good advice from me: don't, for your own safety - you've been warned
			result.DefaultRequestHeaders.UserAgent.ParseAdd(SharedInfo.PublicIdentifier + "/" + SharedInfo.Version + " (+" + SharedInfo.ProjectURL + ")");

			return result;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<HtmlDocumentResponse> UrlGetToHtmlDocument(string request, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			HtmlDocumentResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				await using StreamResponse response = await UrlGetToStream(request, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new HtmlDocumentResponse(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				try {
					result = await HtmlDocumentResponse.Create(response).ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				return result;
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<ObjectResponse<T>> UrlGetToJsonObject<T>(string request, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			ObjectResponse<T> result = null;

			for (byte i = 0; i < maxTries; i++) {
				await using StreamResponse response = await UrlGetToStream(request, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new ObjectResponse<T>(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				T obj;

				try {
					using StreamReader streamReader = new StreamReader(response.Content);
					using JsonReader jsonReader = new JsonTextReader(streamReader);
					JsonSerializer serializer = new JsonSerializer();

					obj = serializer.Deserialize<T>(jsonReader);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				return new ObjectResponse<T>(response, obj);
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<XmlDocumentResponse> UrlGetToXmlDocument(string request, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			XmlDocumentResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				await using StreamResponse response = await UrlGetToStream(request, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new XmlDocumentResponse(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				XmlDocument xmlDocument = new XmlDocument();

				try {
					xmlDocument.Load(response.Content);
				} catch (XmlException e) {
					ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				return new XmlDocumentResponse(response, xmlDocument);
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<BasicResponse> UrlHead(string request, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			BasicResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				using HttpResponseMessage response = await InternalHead(request, referer).ConfigureAwait(false);

				if (response == null) {
					continue;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new BasicResponse(response);
					}

					break;
				}

				return new BasicResponse(response);
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<BasicResponse> UrlPost<T>(string request, T data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			BasicResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				using HttpResponseMessage response = await InternalPost(request, data, referer).ConfigureAwait(false);

				if (response == null) {
					continue;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new BasicResponse(response);
					}

					break;
				}

				return new BasicResponse(response);
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<HtmlDocumentResponse> UrlPostToHtmlDocument<T>(string request, T data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			HtmlDocumentResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				await using StreamResponse response = await UrlPostToStream(request, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new HtmlDocumentResponse(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				try {
					result = await HtmlDocumentResponse.Create(response).ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				return result;
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<ObjectResponse<TResult>> UrlPostToJsonObject<TResult, TData>(string request, TData data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where TResult : class where TData : class {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			ObjectResponse<TResult> result = null;

			for (byte i = 0; i < maxTries; i++) {
				await using StreamResponse response = await UrlPostToStream(request, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new ObjectResponse<TResult>(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				TResult obj;

				try {
					using StreamReader steamReader = new StreamReader(response.Content);
					using JsonReader jsonReader = new JsonTextReader(steamReader);
					JsonSerializer serializer = new JsonSerializer();

					obj = serializer.Deserialize<TResult>(jsonReader);
				} catch (Exception e) {
					ArchiLogger.LogGenericWarningException(e);

					continue;
				}

				return new ObjectResponse<TResult>(response, obj);
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		internal static void Init() {
			// Set max connection limit from default of 2 to desired value
			ServicePointManager.DefaultConnectionLimit = MaxConnections;

			// Set max idle time from default of 100 seconds (100 * 1000) to desired value
			ServicePointManager.MaxServicePointIdleTime = MaxIdleTime * 1000;

			// Don't use Expect100Continue, we're sure about our POSTs, save some TCP packets
			ServicePointManager.Expect100Continue = false;

			// Reuse ports if possible
			if (!RuntimeCompatibility.IsRunningOnMono) {
				ServicePointManager.ReusePort = true;
			}
		}

		internal static async Task<IDocument> StringToHtmlDocument(string html) {
			if (string.IsNullOrEmpty(html)) {
				ASF.ArchiLogger.LogNullError(nameof(html));

				return null;
			}

			IBrowsingContext context = BrowsingContext.New();

			return await context.OpenAsync(req => req.Content(html)).ConfigureAwait(false);
		}

		[ItemCanBeNull]
		internal async Task<BinaryResponse> UrlGetToBinaryWithProgress(string request, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			BinaryResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				const byte printPercentage = 10;
				const byte maxBatches = 99 / printPercentage;

				await using StreamResponse response = await UrlGetToStream(request, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new BinaryResponse(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				ArchiLogger.LogGenericDebug("0%...");

#if !NETFRAMEWORK
				await
#endif
					using MemoryStream ms = new MemoryStream((int) response.Length);

				try {
					byte batch = 0;
					uint readThisBatch = 0;
					byte[] buffer = new byte[8192]; // This is HttpClient's buffer, using more doesn't make sense

					while (response.Content.CanRead) {
						int read = await response.Content.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

						if (read == 0) {
							break;
						}

						await ms.WriteAsync(buffer, 0, read).ConfigureAwait(false);

						if ((response.Length == 0) || (batch >= maxBatches)) {
							continue;
						}

						readThisBatch += (uint) read;

						if (readThisBatch < response.Length / printPercentage) {
							continue;
						}

						readThisBatch -= response.Length / printPercentage;
						ArchiLogger.LogGenericDebug((++batch * printPercentage) + "%...");
					}
				} catch (Exception e) {
					ArchiLogger.LogGenericDebuggingException(e);

					return null;
				}

				ArchiLogger.LogGenericDebug("100%");

				return new BinaryResponse(response, ms.ToArray());
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		internal async Task<StringResponse> UrlGetToString(string request, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			StringResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				using HttpResponseMessage response = await InternalGet(request, referer).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new StringResponse(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				return new StringResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		private async Task<HttpResponseMessage> InternalGet(string request, string referer = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));

				return null;
			}

			return await InternalRequest<object>(new Uri(request), HttpMethod.Get, null, referer, httpCompletionOption).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> InternalHead(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));

				return null;
			}

			return await InternalRequest<object>(new Uri(request), HttpMethod.Head, null, referer).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> InternalPost<T>(string request, T data = null, string referer = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead) where T : class {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));

				return null;
			}

			return await InternalRequest(new Uri(request), HttpMethod.Post, data, referer, httpCompletionOption).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> InternalRequest<T>(Uri requestUri, HttpMethod httpMethod, T data = null, string referer = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, byte maxRedirections = MaxTries) where T : class {
			if ((requestUri == null) || (httpMethod == null)) {
				ArchiLogger.LogNullError(nameof(requestUri) + " || " + nameof(httpMethod));

				return null;
			}

			HttpResponseMessage response;

			using (HttpRequestMessage request = new HttpRequestMessage(httpMethod, requestUri)) {
#if !NETFRAMEWORK
				request.Version = HttpClient.DefaultRequestVersion;
#endif

				if (data != null) {
					switch (data) {
						case HttpContent content:
							request.Content = content;

							break;
						case IReadOnlyCollection<KeyValuePair<string, string>> dictionary:
							try {
								request.Content = new FormUrlEncodedContent(dictionary);
							} catch (UriFormatException) {
								request.Content = new StringContent(string.Join("&", dictionary.Select(kv => WebUtility.UrlEncode(kv.Key) + "=" + WebUtility.UrlEncode(kv.Value))), null, "application/x-www-form-urlencoded");
							}

							break;
						case string text:
							request.Content = new StringContent(text);

							break;
						default:
							request.Content = new StringContent(JsonConvert.SerializeObject(data), Encoding.UTF8, "application/json");

							break;
					}
				}

				if (!string.IsNullOrEmpty(referer)) {
					request.Headers.Referrer = new Uri(referer);
				}

				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug(httpMethod + " " + requestUri);
				}

				try {
					response = await HttpClient.SendAsync(request, httpCompletionOption).ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericDebuggingException(e);

					return null;
				}
			}

			if (response == null) {
				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug("null <- " + httpMethod + " " + requestUri);
				}

				return null;
			}

			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(response.StatusCode + " <- " + httpMethod + " " + requestUri);
			}

			if (response.IsSuccessStatusCode) {
				return response;
			}

			// WARNING: We still have not disposed response by now, make sure to dispose it ASAP if we're not returning it!
			if ((response.StatusCode >= HttpStatusCode.Ambiguous) && (response.StatusCode < HttpStatusCode.BadRequest) && (maxRedirections > 0)) {
				Uri redirectUri = response.Headers.Location;

				if (redirectUri.IsAbsoluteUri) {
					switch (redirectUri.Scheme) {
						case "http":
						case "https":
							break;
						case "steammobile":
							// Those redirections are invalid, but we're aware of that and we have extra logic for them
							return response;
						default:
							// We have no clue about those, but maybe HttpClient can handle them for us
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(redirectUri.Scheme), redirectUri.Scheme));

							break;
					}
				} else {
					redirectUri = new Uri(requestUri, redirectUri);
				}

				response.Dispose();

				// Per https://tools.ietf.org/html/rfc7231#section-7.1.2, a redirect location without a fragment should inherit the fragment from the original URI
				if (!string.IsNullOrEmpty(requestUri.Fragment) && string.IsNullOrEmpty(redirectUri.Fragment)) {
					redirectUri = new UriBuilder(redirectUri) { Fragment = requestUri.Fragment }.Uri;
				}

				return await InternalRequest(redirectUri, httpMethod, data, referer, httpCompletionOption, --maxRedirections).ConfigureAwait(false);
			}

			if (!Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(response.StatusCode + " <- " + httpMethod + " " + requestUri);
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug(string.Format(Strings.Content, await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
				}

				// Do not retry on client errors
				return response;
			}

			using (response) {
				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug(string.Format(Strings.Content, await response.Content.ReadAsStringAsync().ConfigureAwait(false)));
				}

				return null;
			}
		}

		[ItemCanBeNull]
		private async Task<StreamResponse> UrlGetToStream(string request, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			StreamResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				HttpResponseMessage response = await InternalGet(request, referer, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new StreamResponse(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				return new StreamResponse(response, await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		[ItemCanBeNull]
		private async Task<StreamResponse> UrlPostToStream<T>(string request, T data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			StreamResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				HttpResponseMessage response = await InternalPost(request, data, referer, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

				if (response?.StatusCode.IsClientErrorCode() == true) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new StreamResponse(response);
					}

					break;
				}

				if (response?.Content == null) {
					continue;
				}

				return new StreamResponse(response, await response.Content.ReadAsStreamAsync().ConfigureAwait(false));
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		public class BasicResponse {
			[PublicAPI]
			public readonly HttpStatusCode StatusCode;

			internal readonly Uri FinalUri;

			internal BasicResponse([NotNull] HttpResponseMessage httpResponseMessage) {
				if (httpResponseMessage == null) {
					throw new ArgumentNullException(nameof(httpResponseMessage));
				}

				FinalUri = httpResponseMessage.Headers.Location ?? httpResponseMessage.RequestMessage.RequestUri;
				StatusCode = httpResponseMessage.StatusCode;
			}

			internal BasicResponse([NotNull] BasicResponse basicResponse) {
				if (basicResponse == null) {
					throw new ArgumentNullException(nameof(basicResponse));
				}

				FinalUri = basicResponse.FinalUri;
				StatusCode = basicResponse.StatusCode;
			}
		}

		public sealed class HtmlDocumentResponse : BasicResponse, IDisposable {
			[PublicAPI]
			public readonly IDocument Content;

			internal HtmlDocumentResponse([NotNull] BasicResponse basicResponse) : base(basicResponse) {
				if (basicResponse == null) {
					throw new ArgumentNullException(nameof(basicResponse));
				}
			}

			private HtmlDocumentResponse([NotNull] StreamResponse streamResponse, [NotNull] IDocument document) : this(streamResponse) {
				if ((streamResponse == null) || (document == null)) {
					throw new ArgumentNullException(nameof(streamResponse) + " || " + nameof(document));
				}

				Content = document;
			}

			public void Dispose() => Content?.Dispose();

			[ItemCanBeNull]
			internal static async Task<HtmlDocumentResponse> Create([NotNull] StreamResponse streamResponse) {
				if (streamResponse == null) {
					throw new ArgumentNullException(nameof(streamResponse));
				}

				IBrowsingContext context = BrowsingContext.New();

				try {
					IDocument document = await context.OpenAsync(req => req.Content(streamResponse.Content, true)).ConfigureAwait(false);

					return new HtmlDocumentResponse(streamResponse, document);
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericWarningException(e);

					return null;
				}
			}
		}

		public sealed class ObjectResponse<T> : BasicResponse {
			[PublicAPI]
			public readonly T Content;

			internal ObjectResponse([NotNull] StreamResponse streamResponse, T content) : this(streamResponse) {
				if (streamResponse == null) {
					throw new ArgumentNullException(nameof(streamResponse));
				}

				Content = content;
			}

			internal ObjectResponse([NotNull] BasicResponse basicResponse) : base(basicResponse) {
				if (basicResponse == null) {
					throw new ArgumentNullException(nameof(basicResponse));
				}
			}
		}

		public sealed class XmlDocumentResponse : BasicResponse {
			[PublicAPI]
			public readonly XmlDocument Content;

			internal XmlDocumentResponse([NotNull] StreamResponse streamResponse, XmlDocument content) : this(streamResponse) {
				if (streamResponse == null) {
					throw new ArgumentNullException(nameof(streamResponse));
				}

				Content = content;
			}

			internal XmlDocumentResponse([NotNull] BasicResponse basicResponse) : base(basicResponse) {
				if (basicResponse == null) {
					throw new ArgumentNullException(nameof(basicResponse));
				}
			}
		}

		[Flags]
		public enum ERequestOptions : byte {
			None = 0,
			ReturnClientErrors = 1
		}

		internal sealed class BinaryResponse : BasicResponse {
			internal readonly byte[] Content;

			internal BinaryResponse([NotNull] BasicResponse basicResponse, [NotNull] byte[] content) : this(basicResponse) {
				if ((basicResponse == null) || (content == null)) {
					throw new ArgumentNullException(nameof(basicResponse) + " || " + nameof(content));
				}

				Content = content;
			}

			internal BinaryResponse([NotNull] BasicResponse basicResponse) : base(basicResponse) {
				if (basicResponse == null) {
					throw new ArgumentNullException(nameof(basicResponse));
				}
			}
		}

		internal sealed class StreamResponse : BasicResponse, IAsyncDisposable {
			internal readonly Stream Content;
			internal readonly uint Length;

			private readonly HttpResponseMessage ResponseMessage;

			internal StreamResponse([NotNull] HttpResponseMessage httpResponseMessage, [NotNull] Stream content) : this(httpResponseMessage) {
				if ((httpResponseMessage == null) || (content == null)) {
					throw new ArgumentNullException(nameof(httpResponseMessage) + " || " + nameof(content));
				}

				Content = content;
			}

			internal StreamResponse([NotNull] HttpResponseMessage httpResponseMessage) : base(httpResponseMessage) {
				if (httpResponseMessage == null) {
					throw new ArgumentNullException(nameof(httpResponseMessage));
				}

				Length = (uint) httpResponseMessage.Content.Headers.ContentLength.GetValueOrDefault();
				ResponseMessage = httpResponseMessage;
			}

			public async ValueTask DisposeAsync() {
				if (Content != null) {
					await Content.DisposeAsync().ConfigureAwait(false);
				}

				ResponseMessage.Dispose();
			}
		}

		internal sealed class StringResponse : BasicResponse {
			internal readonly string Content;

			internal StringResponse([NotNull] HttpResponseMessage httpResponseMessage, [NotNull] string content) : this(httpResponseMessage) {
				if ((httpResponseMessage == null) || (content == null)) {
					throw new ArgumentNullException(nameof(httpResponseMessage) + " || " + nameof(content));
				}

				Content = content;
			}

			internal StringResponse([NotNull] HttpResponseMessage httpResponseMessage) : base(httpResponseMessage) {
				if (httpResponseMessage == null) {
					throw new ArgumentNullException(nameof(httpResponseMessage));
				}
			}
		}
	}
}
