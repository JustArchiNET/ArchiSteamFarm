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
using System.Threading.Tasks;
using System.Xml;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using HtmlAgilityPack;
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
				AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
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

			StringResponse response = await UrlGetToString(request, referer, requestOptions, maxTries).ConfigureAwait(false);

			return response != null ? new HtmlDocumentResponse(response) : null;
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
				StringResponse response = await UrlGetToString(request, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				// ReSharper disable once UseNullPropagationWhenPossible - false check
				if (response == null) {
					continue;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new ObjectResponse<T>(response);
					}

					break;
				}

				if (string.IsNullOrEmpty(response.Content)) {
					continue;
				}

				T obj;

				try {
					obj = JsonConvert.DeserializeObject<T>(response.Content);
				} catch (JsonException e) {
					ArchiLogger.LogGenericWarningException(e);

					if (Debugging.IsUserDebugging) {
						ArchiLogger.LogGenericDebug(string.Format(Strings.Content, response.Content));
					}

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
				StringResponse response = await UrlGetToString(request, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				// ReSharper disable once UseNullPropagationWhenPossible - false check
				if (response == null) {
					continue;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new XmlDocumentResponse(response);
					}

					break;
				}

				if (string.IsNullOrEmpty(response.Content)) {
					continue;
				}

				XmlDocument xmlDocument = new XmlDocument();

				try {
					xmlDocument.LoadXml(response.Content);
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
		public async Task<BasicResponse> UrlPost(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
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
		public async Task<HtmlDocumentResponse> UrlPostToHtmlDocument(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			StringResponse response = await UrlPostToString(request, data, referer, requestOptions, maxTries).ConfigureAwait(false);

			return response != null ? new HtmlDocumentResponse(response) : null;
		}

		[ItemCanBeNull]
		[PublicAPI]
		public async Task<ObjectResponse<T>> UrlPostToJsonObject<T>(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) where T : class {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			ObjectResponse<T> result = null;

			for (byte i = 0; i < maxTries; i++) {
				StringResponse response = await UrlPostToString(request, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1).ConfigureAwait(false);

				if (response == null) {
					return null;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new ObjectResponse<T>(response);
					}

					break;
				}

				if (string.IsNullOrEmpty(response.Content)) {
					continue;
				}

				T obj;

				try {
					obj = JsonConvert.DeserializeObject<T>(response.Content);
				} catch (JsonException e) {
					ArchiLogger.LogGenericWarningException(e);

					if (Debugging.IsUserDebugging) {
						ArchiLogger.LogGenericDebug(string.Format(Strings.Content, response.Content));
					}

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

		internal static HtmlDocument StringToHtmlDocument(string html) {
			if (html == null) {
				ASF.ArchiLogger.LogNullError(nameof(html));

				return null;
			}

			HtmlDocument htmlDocument = new HtmlDocument();
			htmlDocument.LoadHtml(html);

			return htmlDocument;
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

				using HttpResponseMessage response = await InternalGet(request, referer, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);

				if (response == null) {
					continue;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new BinaryResponse(response);
					}

					break;
				}

				ArchiLogger.LogGenericDebug("0%...");

				uint contentLength = (uint) response.Content.Headers.ContentLength.GetValueOrDefault();

				using MemoryStream ms = new MemoryStream((int) contentLength);

				try {
					using Stream contentStream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

					byte batch = 0;
					uint readThisBatch = 0;
					byte[] buffer = new byte[8192]; // This is HttpClient's buffer, using more doesn't make sense

					while (contentStream.CanRead) {
						int read = await contentStream.ReadAsync(buffer, 0, buffer.Length).ConfigureAwait(false);

						if (read == 0) {
							break;
						}

						await ms.WriteAsync(buffer, 0, read).ConfigureAwait(false);

						if ((contentLength == 0) || (batch >= maxBatches)) {
							continue;
						}

						readThisBatch += (uint) read;

						if (readThisBatch < contentLength / printPercentage) {
							continue;
						}

						readThisBatch -= contentLength / printPercentage;
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

				if (response == null) {
					continue;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new StringResponse(response);
					}

					break;
				}

				return new StringResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
			}

			if (maxTries > 1) {
				ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, maxTries));
				ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			}

			return result;
		}

		private async Task<HttpResponseMessage> InternalGet(string request, string referer = null, HttpCompletionOption httpCompletionOptions = HttpCompletionOption.ResponseContentRead) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));

				return null;
			}

			return await InternalRequest(new Uri(request), HttpMethod.Get, null, referer, httpCompletionOptions).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> InternalHead(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));

				return null;
			}

			return await InternalRequest(new Uri(request), HttpMethod.Head, null, referer).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> InternalPost(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));

				return null;
			}

			return await InternalRequest(new Uri(request), HttpMethod.Post, data, referer).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> InternalRequest(Uri requestUri, HttpMethod httpMethod, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, byte maxRedirections = MaxTries) {
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
					try {
						request.Content = new FormUrlEncodedContent(data);
					} catch (UriFormatException) {
						request.Content = new StringContent(string.Join("&", data.Select(kv => WebUtility.UrlEncode(kv.Key) + "=" + WebUtility.UrlEncode(kv.Value))), null, "application/x-www-form-urlencoded");
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
		private async Task<StringResponse> UrlPostToString(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries) {
			if (string.IsNullOrEmpty(request) || (maxTries == 0)) {
				ArchiLogger.LogNullError(nameof(request) + " || " + nameof(maxTries));

				return null;
			}

			StringResponse result = null;

			for (byte i = 0; i < maxTries; i++) {
				using HttpResponseMessage response = await InternalPost(request, data, referer).ConfigureAwait(false);

				if (response == null) {
					continue;
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						result = new StringResponse(response);
					}

					break;
				}

				return new StringResponse(response, await response.Content.ReadAsStringAsync().ConfigureAwait(false));
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

		public sealed class HtmlDocumentResponse : BasicResponse {
			[PublicAPI]
			public readonly HtmlDocument Content;

			internal HtmlDocumentResponse([NotNull] StringResponse stringResponse) : base(stringResponse) {
				if (stringResponse == null) {
					throw new ArgumentNullException(nameof(stringResponse));
				}

				if (!string.IsNullOrEmpty(stringResponse.Content)) {
					Content = StringToHtmlDocument(stringResponse.Content);
				}
			}
		}

		public sealed class ObjectResponse<T> : BasicResponse {
			[PublicAPI]
			public readonly T Content;

			internal ObjectResponse([NotNull] StringResponse stringResponse, T content) : base(stringResponse) {
				if (stringResponse == null) {
					throw new ArgumentNullException(nameof(stringResponse));
				}

				Content = content;
			}

			internal ObjectResponse([NotNull] BasicResponse basicResponse) : base(basicResponse) { }
		}

		public sealed class XmlDocumentResponse : BasicResponse {
			[PublicAPI]
			public readonly XmlDocument Content;

			internal XmlDocumentResponse([NotNull] StringResponse stringResponse, XmlDocument content) : base(stringResponse) {
				if (stringResponse == null) {
					throw new ArgumentNullException(nameof(stringResponse));
				}

				Content = content;
			}

			internal XmlDocumentResponse([NotNull] BasicResponse basicResponse) : base(basicResponse) { }
		}

		[Flags]
		public enum ERequestOptions : byte {
			None = 0,
			ReturnClientErrors = 1
		}

		internal sealed class BinaryResponse : BasicResponse {
			internal readonly byte[] Content;

			internal BinaryResponse([NotNull] HttpResponseMessage httpResponseMessage, [NotNull] byte[] content) : base(httpResponseMessage) {
				if ((httpResponseMessage == null) || (content == null)) {
					throw new ArgumentNullException(nameof(httpResponseMessage) + " || " + nameof(content));
				}

				Content = content;
			}

			internal BinaryResponse([NotNull] HttpResponseMessage httpResponseMessage) : base(httpResponseMessage) { }
		}

		internal sealed class StringResponse : BasicResponse {
			internal readonly string Content;

			internal StringResponse([NotNull] HttpResponseMessage httpResponseMessage, [NotNull] string content) : base(httpResponseMessage) {
				if ((httpResponseMessage == null) || (content == null)) {
					throw new ArgumentNullException(nameof(httpResponseMessage) + " || " + nameof(content));
				}

				Content = content;
			}

			internal StringResponse([NotNull] HttpResponseMessage httpResponseMessage) : base(httpResponseMessage) { }
		}
	}
}
