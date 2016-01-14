/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
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
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace ArchiSteamFarm {
	internal static class WebBrowser {
		private const string FakeUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/47.0.2526.106 Safari/537.36";

		internal const byte MaxRetries = 5;
		internal const byte HttpTimeout = 180; // In seconds

		private static readonly HttpClientHandler HttpClientHandler = new HttpClientHandler {
			AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
			UseCookies = false
		};

		private static readonly HttpClient HttpClient = new HttpClient(HttpClientHandler) { Timeout = TimeSpan.FromSeconds(HttpTimeout) };

		internal static void Init() {
			// Declare default UserAgent
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ArchiSteamFarm/" + Program.Version);

			// Some web servers might go crazy if we don't specify some extra headers
			HttpClient.DefaultRequestHeaders.Add("X-Requested-With", "XMLHttpRequest");

			// Increase limit of maximum number of allowed concurrent connections
			// Default is 2 which is usually too low for what we're doing
			ServicePointManager.DefaultConnectionLimit = int.MaxValue;

			// Don't use Expect100Continue, we don't need to do that
			ServicePointManager.Expect100Continue = false;
		}

		private static async Task<HttpResponseMessage> UrlRequest(string request, HttpMethod httpMethod, Dictionary<string, string> data = null, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request) || httpMethod == null) {
				return null;
			}

			HttpRequestMessage requestMessage = new HttpRequestMessage(httpMethod, request);

			if (data != null) {
				requestMessage.Content = new FormUrlEncodedContent(data);
			}

			if (cookies != null && cookies.Count > 0) {
				StringBuilder cookieHeader = new StringBuilder();
				foreach (KeyValuePair<string, string> cookie in cookies) {
					cookieHeader.Append(cookie.Key + "=" + cookie.Value + ";");
				}
				requestMessage.Headers.Add("Cookie", cookieHeader.ToString());
			}

			if (referer != null) {
				requestMessage.Headers.Referrer = new Uri(referer);
			}

			if (fakeUserAgent) {
				requestMessage.Headers.UserAgent.ParseAdd(FakeUserAgent);
			}

			HttpResponseMessage responseMessage;

			try {
				responseMessage = await HttpClient.SendAsync(requestMessage).ConfigureAwait(false);
			} catch { // Request failed, we don't need to know the exact reason, swallow exception
				return null;
			}

			if (responseMessage == null || !responseMessage.IsSuccessStatusCode) {
				return null;
			}

			return responseMessage;
		}

		internal static async Task<HttpResponseMessage> UrlGet(string request, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			return await UrlRequest(request, HttpMethod.Get, null, cookies, referer, fakeUserAgent).ConfigureAwait(false);
		}

		internal static async Task<HttpResponseMessage> UrlPost(string request, Dictionary<string, string> data = null, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			return await UrlRequest(request, HttpMethod.Post, data, cookies, referer, fakeUserAgent).ConfigureAwait(false);
		}

		internal static async Task<HtmlDocument> HttpResponseToHtmlDocument(HttpResponseMessage httpResponse) {
			if (httpResponse == null) {
				return null;
			}

			HttpContent httpContent = httpResponse.Content;
			if (httpContent == null) {
				return null;
			}

			string content = await httpContent.ReadAsStringAsync().ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			content = WebUtility.HtmlDecode(content);
			HtmlDocument htmlDocument = new HtmlDocument();
			htmlDocument.LoadHtml(content);

			return htmlDocument;
		}

		internal static async Task<string> UrlGetToContent(string request, Dictionary<string, string> cookies, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HttpResponseMessage httpResponse = await UrlGet(request, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (httpResponse == null) {
				return null;
			}

			HttpContent httpContent = httpResponse.Content;
			if (httpContent == null) {
				return null;
			}

			return await httpContent.ReadAsStringAsync().ConfigureAwait(false);
		}

		internal static async Task<string> UrlPostToContent(string request, Dictionary<string, string> data = null, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HttpResponseMessage httpResponse = await UrlPost(request, data, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (httpResponse == null) {
				return null;
			}

			HttpContent httpContent = httpResponse.Content;
			if (httpContent == null) {
				return null;
			}

			return await httpContent.ReadAsStringAsync().ConfigureAwait(false);
		}

		internal static async Task<JObject> UrlPostToJObject(string request, Dictionary<string, string> data = null, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlPostToContent(request, data, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			JObject jObject;

			try {
				jObject = JObject.Parse(content);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}

			return jObject;
		}

		internal static async Task<HtmlDocument> UrlGetToHtmlDocument(string request, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HttpResponseMessage httpResponse = await UrlGet(request, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (httpResponse == null) {
				return null;
			}

			return await HttpResponseToHtmlDocument(httpResponse).ConfigureAwait(false);
		}

		internal static async Task<HtmlDocument> UrlPostToHtmlDocument(string request, Dictionary<string, string> data = null, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HttpResponseMessage httpResponse = await UrlPost(request, data, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (httpResponse == null) {
				return null;
			}

			return await HttpResponseToHtmlDocument(httpResponse).ConfigureAwait(false);
		}

		internal static async Task<string> UrlGetToTitle(string request, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HtmlDocument htmlDocument = await UrlGetToHtmlDocument(request, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (htmlDocument == null) {
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//head/title");
			if (htmlNode == null) {
				return null;
			}

			return htmlNode.InnerText;
		}

		internal static async Task<JArray> UrlGetToJArray(string request, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			JArray jArray;

			try {
				jArray = JArray.Parse(content);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}

			return jArray;
		}

		internal static async Task<JObject> UrlGetToJObject(string request, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			JObject jObject;

			try {
				jObject = JObject.Parse(content);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}

			return jObject;
		}

		internal static async Task<XmlDocument> UrlGetToXML(string request, Dictionary<string, string> cookies = null, string referer = null, bool fakeUserAgent = false) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, cookies, referer, fakeUserAgent).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			XmlDocument xmlDocument = new XmlDocument();

			try {
				xmlDocument.LoadXml(content);
			} catch (XmlException e) {
				Logging.LogGenericException(e);
				return null;
			}

			return xmlDocument;
		}
	}
}
