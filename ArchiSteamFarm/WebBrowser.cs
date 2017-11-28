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
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;
using ArchiSteamFarm.Localization;
using HtmlAgilityPack;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal sealed class WebBrowser : IDisposable {
		internal const byte MaxTries = 5; // Defines maximum number of recommended tries for a single request

		private const byte ExtendedTimeoutMultiplier = 10; // Defines multiplier of timeout for WebBrowsers dealing with huge data (ASF update)
		private const byte MaxConnections = 10; // Defines maximum number of connections per ServicePoint. Be careful, as it also defines maximum number of sockets in CLOSE_WAIT state
		private const byte MaxIdleTime = 15; // Defines in seconds, how long socket is allowed to stay in CLOSE_WAIT state after there are no connections to it

		internal readonly CookieContainer CookieContainer = new CookieContainer();

		internal TimeSpan Timeout => HttpClient.Timeout;

		private readonly ArchiLogger ArchiLogger;
		private readonly HttpClient HttpClient;

		internal WebBrowser(ArchiLogger archiLogger, bool extendedTimeout = false) {
			ArchiLogger = archiLogger ?? throw new ArgumentNullException(nameof(archiLogger));

			HttpClientHandler httpClientHandler = new HttpClientHandler {
				AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
				CookieContainer = CookieContainer,
				MaxConnectionsPerServer = MaxConnections
			};

			HttpClient = new HttpClient(httpClientHandler) { Timeout = TimeSpan.FromSeconds(extendedTimeout ? ExtendedTimeoutMultiplier * Program.GlobalConfig.ConnectionTimeout : Program.GlobalConfig.ConnectionTimeout) };

			// Most web services expect that UserAgent is set, so we declare it globally
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(SharedInfo.AssemblyName + "/" + SharedInfo.Version);
		}

		public void Dispose() => HttpClient.Dispose();

		internal static void Init() {
			// Set max connection limit from default of 2 to desired value
			ServicePointManager.DefaultConnectionLimit = MaxConnections;

			// Set max idle time from default of 100 seconds (100 * 1000) to desired value
			ServicePointManager.MaxServicePointIdleTime = MaxIdleTime * 1000;

			// Don't use Expect100Continue, we're sure about our POSTs, save some TCP packets
			ServicePointManager.Expect100Continue = false;

			// Reuse ports if possible
			ServicePointManager.ReusePort = true;
		}

		internal static HtmlDocument StringToHtmlDocument(string html) {
			if (string.IsNullOrEmpty(html)) {
				ASF.ArchiLogger.LogNullError(nameof(html));
				return null;
			}

			HtmlDocument htmlDocument = new HtmlDocument();
			htmlDocument.LoadHtml(html);

			return htmlDocument;
		}

		internal async Task<byte[]> UrlGetToBytesRetry(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			byte[] result = null;
			for (byte i = 0; (i < MaxTries) && (result == null); i++) {
				result = await UrlGetToBytes(request, referer).ConfigureAwait(false);
			}

			if (result != null) {
				return result;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return null;
		}

		internal async Task<HtmlDocument> UrlGetToHtmlDocumentRetry(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			HtmlDocument result = null;
			for (byte i = 0; (i < MaxTries) && (result == null); i++) {
				result = await UrlGetToHtmlDocument(request, referer).ConfigureAwait(false);
			}

			if (result != null) {
				return result;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return null;
		}

		internal async Task<T> UrlGetToJsonResultRetry<T>(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return default;
			}

			string json = await UrlGetToContentRetry(request, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(json)) {
				return default;
			}

			try {
				return JsonConvert.DeserializeObject<T>(json);
			} catch (JsonException e) {
				ArchiLogger.LogGenericException(e);

				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug(string.Format(Strings.Content, json));
				}

				return default;
			}
		}

		internal async Task<XmlDocument> UrlGetToXMLRetry(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			XmlDocument result = null;
			for (byte i = 0; (i < MaxTries) && (result == null); i++) {
				result = await UrlGetToXML(request, referer).ConfigureAwait(false);
			}

			if (result != null) {
				return result;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return null;
		}

		internal async Task<bool> UrlHeadRetry(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return false;
			}

			bool result = false;
			for (byte i = 0; (i < MaxTries) && !result; i++) {
				result = await UrlHead(request, referer).ConfigureAwait(false);
			}

			if (result) {
				return true;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return false;
		}

		internal async Task<Uri> UrlHeadToUriRetry(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			Uri result = null;
			for (byte i = 0; (i < MaxTries) && (result == null); i++) {
				result = await UrlHeadToUri(request, referer).ConfigureAwait(false);
			}

			if (result != null) {
				return result;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return null;
		}

		internal async Task<bool> UrlPost(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return false;
			}

			using (HttpResponseMessage response = await UrlPostToResponse(request, data, referer).ConfigureAwait(false)) {
				return response != null;
			}
		}

		internal async Task<bool> UrlPostRetry(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return false;
			}

			bool result = false;
			for (byte i = 0; (i < MaxTries) && !result; i++) {
				result = await UrlPost(request, data, referer).ConfigureAwait(false);
			}

			if (result) {
				return true;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return false;
		}

		internal async Task<HtmlDocument> UrlPostToHtmlDocumentRetry(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			string content = await UrlPostToContentRetry(request, data, referer).ConfigureAwait(false);
			return !string.IsNullOrEmpty(content) ? StringToHtmlDocument(content) : null;
		}

		internal async Task<T> UrlPostToJsonResultRetry<T>(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return default;
			}

			string json = await UrlPostToContentRetry(request, data, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(json)) {
				return default;
			}

			try {
				return JsonConvert.DeserializeObject<T>(json);
			} catch (JsonException e) {
				ArchiLogger.LogGenericException(e);

				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug(string.Format(Strings.Content, json));
				}

				return default;
			}
		}

		private async Task<byte[]> UrlGetToBytes(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			using (HttpResponseMessage httpResponse = await UrlGetToResponse(request, referer).ConfigureAwait(false)) {
				if (httpResponse == null) {
					return null;
				}

				return await httpResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
			}
		}

		private async Task<string> UrlGetToContent(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			using (HttpResponseMessage httpResponse = await UrlGetToResponse(request, referer).ConfigureAwait(false)) {
				if (httpResponse == null) {
					return null;
				}

				return await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
			}
		}

		private async Task<string> UrlGetToContentRetry(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			string result = null;
			for (byte i = 0; (i < MaxTries) && string.IsNullOrEmpty(result); i++) {
				result = await UrlGetToContent(request, referer).ConfigureAwait(false);
			}

			if (!string.IsNullOrEmpty(result)) {
				return result;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return null;
		}

		private async Task<HtmlDocument> UrlGetToHtmlDocument(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			string content = await UrlGetToContent(request, referer).ConfigureAwait(false);
			return !string.IsNullOrEmpty(content) ? StringToHtmlDocument(content) : null;
		}

		private async Task<HttpResponseMessage> UrlGetToResponse(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			HttpResponseMessage result = await UrlRequest(new Uri(request), HttpMethod.Get, null, referer).ConfigureAwait(false);
			return result;
		}

		private async Task<XmlDocument> UrlGetToXML(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			string xml = await UrlGetToContent(request, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(xml)) {
				return null;
			}

			XmlDocument xmlDocument = new XmlDocument();

			try {
				xmlDocument.LoadXml(xml);
			} catch (XmlException e) {
				ArchiLogger.LogGenericException(e);
				return null;
			}

			return xmlDocument;
		}

		private async Task<bool> UrlHead(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return false;
			}

			using (HttpResponseMessage response = await UrlHeadToResponse(request, referer).ConfigureAwait(false)) {
				return response != null;
			}
		}

		private async Task<HttpResponseMessage> UrlHeadToResponse(string request, string referer = null) {
			if (!string.IsNullOrEmpty(request)) {
				return await UrlRequest(new Uri(request), HttpMethod.Head, null, referer).ConfigureAwait(false);
			}

			ArchiLogger.LogNullError(nameof(request));
			return null;
		}

		private async Task<Uri> UrlHeadToUri(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			using (HttpResponseMessage response = await UrlHeadToResponse(request, referer).ConfigureAwait(false)) {
				return response?.RequestMessage.RequestUri;
			}
		}

		private async Task<string> UrlPostToContent(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			using (HttpResponseMessage httpResponse = await UrlPostToResponse(request, data, referer).ConfigureAwait(false)) {
				if (httpResponse == null) {
					return null;
				}

				return await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
			}
		}

		private async Task<string> UrlPostToContentRetry(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				ArchiLogger.LogNullError(nameof(request));
				return null;
			}

			string result = null;
			for (byte i = 0; (i < MaxTries) && string.IsNullOrEmpty(result); i++) {
				result = await UrlPostToContent(request, data, referer).ConfigureAwait(false);
			}

			if (!string.IsNullOrEmpty(result)) {
				return result;
			}

			ArchiLogger.LogGenericWarning(string.Format(Strings.ErrorRequestFailedTooManyTimes, MaxTries));
			ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, request));
			return null;
		}

		private async Task<HttpResponseMessage> UrlPostToResponse(string request, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null) {
			if (!string.IsNullOrEmpty(request)) {
				return await UrlRequest(new Uri(request), HttpMethod.Post, data, referer).ConfigureAwait(false);
			}

			ArchiLogger.LogNullError(nameof(request));
			return null;
		}

		private async Task<HttpResponseMessage> UrlRequest(Uri requestUri, HttpMethod httpMethod, IReadOnlyCollection<KeyValuePair<string, string>> data = null, string referer = null, byte maxRedirections = MaxTries) {
			if ((requestUri == null) || (httpMethod == null)) {
				ArchiLogger.LogNullError(nameof(requestUri) + " || " + nameof(httpMethod));
				return null;
			}

			HttpResponseMessage responseMessage;
			using (HttpRequestMessage requestMessage = new HttpRequestMessage(httpMethod, requestUri)) {
				if (data != null) {
					try {
						requestMessage.Content = new FormUrlEncodedContent(data);
					} catch (UriFormatException e) {
						ArchiLogger.LogGenericException(e);
						return null;
					}
				}

				if (!string.IsNullOrEmpty(referer)) {
					requestMessage.Headers.Referrer = new Uri(referer);
				}

				try {
					responseMessage = await HttpClient.SendAsync(requestMessage).ConfigureAwait(false);
				} catch (Exception e) {
					ArchiLogger.LogGenericDebuggingException(e);
					return null;
				}
			}

			if (responseMessage == null) {
				return null;
			}

			if (responseMessage.IsSuccessStatusCode) {
				return responseMessage;
			}

			Uri redirectUri;
			using (responseMessage) {
				ushort status = (ushort) responseMessage.StatusCode;
				if ((status >= 300) && (status <= 399) && (maxRedirections > 0)) {
					redirectUri = responseMessage.Headers.Location;

					if (redirectUri.IsAbsoluteUri) {
						switch (redirectUri.Scheme) {
							case "http":
							case "https":
								break;
							default:
								// Invalid ones such as "steammobile"
								return null;
						}
					} else {
						redirectUri = new Uri(requestUri.GetLeftPart(UriPartial.Authority) + redirectUri);
					}
				} else {
					if (!Debugging.IsDebugBuild) {
						return null;
					}

					ArchiLogger.LogGenericDebug(string.Format(Strings.ErrorFailingRequest, requestUri));
					ArchiLogger.LogGenericDebug(string.Format(Strings.StatusCode, responseMessage.StatusCode));
					ArchiLogger.LogGenericDebug(string.Format(Strings.Content, await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false)));
					return null;
				}
			}

			return await UrlRequest(redirectUri, httpMethod, data, referer, --maxRedirections).ConfigureAwait(false);
		}
	}
}