/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
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

using HtmlAgilityPack;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml;

namespace ArchiSteamFarm {
	internal sealed class WebBrowser {
		internal const byte MaxRetries = 5; // Defines maximum number of retries, UrlRequest() does not handle retry by itself (it's app responsibility)

		private const byte MaxConnections = 10; // Defines maximum number of connections per ServicePoint. Be careful, as it also defines maximum number of sockets in CLOSE_WAIT state
		private const byte MaxIdleTime = 15; // In seconds, how long socket is allowed to stay in CLOSE_WAIT state after there are no connections to it

		private static readonly string DefaultUserAgent = "ArchiSteamFarm/" + Program.Version;

		internal readonly CookieContainer CookieContainer = new CookieContainer();

		private readonly HttpClient HttpClient;
		private readonly string Identifier;

		internal static void Init() {
			// Set max connection limit from default of 2 to desired value
			ServicePointManager.DefaultConnectionLimit = MaxConnections;

			// Set max idle time from default of 100 seconds (100 * 1000) to desired value
			ServicePointManager.MaxServicePointIdleTime = MaxIdleTime * 1000;

			// Don't use Expect100Continue, we're sure about our POSTs, save some TCP packets
			ServicePointManager.Expect100Continue = false;

#if !__MonoCS__
			// Reuse ports if possible (since .NET 4.6+)
			//ServicePointManager.ReusePort = true;
#endif
		}

		internal WebBrowser(string identifier) {
			if (string.IsNullOrEmpty(identifier)) {
				throw new ArgumentNullException("identifier");
			}

			Identifier = identifier;

			HttpClientHandler httpClientHandler = new HttpClientHandler {
				AutomaticDecompression = DecompressionMethods.Deflate | DecompressionMethods.GZip,
				CookieContainer = CookieContainer
			};

			HttpClient = new HttpClient(httpClientHandler) {
				Timeout = TimeSpan.FromSeconds(Program.GlobalConfig.HttpTimeout)
			};

			// Most web services expect that UserAgent is set, so we declare it globally
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd(DefaultUserAgent);
		}

		internal async Task<bool> UrlHead(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return false;
			}

			using (HttpResponseMessage response = await UrlHeadToResponse(request, referer).ConfigureAwait(false)) {
				return response != null;
			}
		}

		internal async Task<Uri> UrlHeadToUri(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			using (HttpResponseMessage response = await UrlHeadToResponse(request, referer).ConfigureAwait(false)) {
				if (response == null) {
					return null;
				}

				return response.RequestMessage.RequestUri;
			}
		}

		internal async Task<string> UrlGetToContent(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			using (HttpResponseMessage httpResponse = await UrlGetToResponse(request, referer).ConfigureAwait(false)) {
				if (httpResponse == null) {
					return null;
				}

				return await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
			}
		}

		internal async Task<byte[]> UrlGetToBytes(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			using (HttpResponseMessage httpResponse = await UrlGetToResponse(request, referer).ConfigureAwait(false)) {
				if (httpResponse == null) {
					return null;
				}

				return await httpResponse.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
			}
		}

		internal async Task<HtmlDocument> UrlGetToHtmlDocument(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			HtmlDocument htmlDocument = new HtmlDocument();
			htmlDocument.LoadHtml(WebUtility.HtmlDecode(content));
			return htmlDocument;
		}

		internal async Task<JObject> UrlGetToJObject(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			JObject jObject;

			try {
				jObject = JObject.Parse(content);
			} catch (JsonException e) {
				Logging.LogGenericException(e, Identifier);
				return null;
			}

			return jObject;
		}

		internal async Task<XmlDocument> UrlGetToXML(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			XmlDocument xmlDocument = new XmlDocument();

			try {
				xmlDocument.LoadXml(content);
			} catch (XmlException e) {
				Logging.LogGenericException(e, Identifier);
				return null;
			}

			return xmlDocument;
		}

		internal async Task<bool> UrlPost(string request, Dictionary<string, string> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return false;
			}

			using (HttpResponseMessage response = await UrlPostToResponse(request, data, referer).ConfigureAwait(false)) {
				return response != null;
			}
		}

		private async Task<HttpResponseMessage> UrlGetToResponse(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			return await UrlRequest(request, HttpMethod.Get, null, referer).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> UrlHeadToResponse(string request, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			return await UrlRequest(request, HttpMethod.Head, null, referer).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> UrlPostToResponse(string request, Dictionary<string, string> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			return await UrlRequest(request, HttpMethod.Post, data, referer).ConfigureAwait(false);
		}

		private async Task<HttpResponseMessage> UrlRequest(string request, HttpMethod httpMethod, Dictionary<string, string> data = null, string referer = null) {
			if (string.IsNullOrEmpty(request) || httpMethod == null) {
				return null;
			}

			if (request.StartsWith("https://", StringComparison.Ordinal) && Program.GlobalConfig.ForceHttp) {
				return null;
			}

			HttpResponseMessage responseMessage;
			using (HttpRequestMessage requestMessage = new HttpRequestMessage(httpMethod, request)) {
				if (data != null && data.Count > 0) {
					try {
						requestMessage.Content = new FormUrlEncodedContent(data);
					} catch (UriFormatException e) {
						Logging.LogGenericException(e, Identifier);
						return null;
					}
				}

				if (!string.IsNullOrEmpty(referer)) {
					requestMessage.Headers.Referrer = new Uri(referer);
				}

				try {
					responseMessage = await HttpClient.SendAsync(requestMessage).ConfigureAwait(false);
				} catch { // Request failed, we don't need to know the exact reason, swallow exception
					return null;
				}
			}

			if (responseMessage == null) {
				return null;
			}

			if (!responseMessage.IsSuccessStatusCode) {
				if (Debugging.IsDebugBuild || Program.GlobalConfig.Debug) {
					Logging.LogGenericError("Request: " + request + " failed!", Identifier);
					Logging.LogGenericError("Status code: " + responseMessage.StatusCode, Identifier);
					Logging.LogGenericError("Content: " + Environment.NewLine + await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false), Identifier);
				}
				responseMessage.Dispose();
				return null;
			}

			return responseMessage;
		}
	}
}
