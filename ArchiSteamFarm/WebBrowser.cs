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
		internal const byte HttpTimeout = 180; // In seconds

		private static readonly HttpClientHandler HttpClientHandler = new HttpClientHandler { UseCookies = false };
		private static readonly HttpClient HttpClient = new HttpClient(HttpClientHandler) { Timeout = TimeSpan.FromSeconds(HttpTimeout) };

		internal static void Init() {
			HttpClient.DefaultRequestHeaders.UserAgent.ParseAdd("ArchiSteamFarm/" + Program.Version);

			// Don't limit maximum number of allowed concurrent connections
			// It's application's responsibility to handle that stuff
			ServicePointManager.DefaultConnectionLimit = int.MaxValue;

			// Don't use Expect100Continue, we don't need to do that
			ServicePointManager.Expect100Continue = false;
		}

		private static async Task<HttpResponseMessage> UrlRequest(string request, HttpMethod httpMethod, Dictionary<string, string> data = null, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request) || httpMethod == null) {
				return null;
			}

			HttpRequestMessage requestMessage = new HttpRequestMessage(httpMethod, request);

			if (httpMethod == HttpMethod.Post && data != null) {
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

		internal static async Task<HttpResponseMessage> UrlGet(string request, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			return await UrlRequest(request, HttpMethod.Get, null, cookies, referer).ConfigureAwait(false);
		}

		internal static async Task<HttpResponseMessage> UrlPost(string request, Dictionary<string, string> postData = null, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			return await UrlRequest(request, HttpMethod.Post, postData, cookies, referer).ConfigureAwait(false);
		}

		internal static async Task<HtmlDocument> HttpResponseToHtmlDocument(HttpResponseMessage httpResponse) {
			if (httpResponse == null || httpResponse.Content == null) {
				return null;
			}

			string content = await httpResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			content = WebUtility.HtmlDecode(content);
			HtmlDocument htmlDocument = new HtmlDocument();
			htmlDocument.LoadHtml(content);

			return htmlDocument;
		}

		internal static async Task<string> UrlGetToContent(string request, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HttpResponseMessage responseMessage = await UrlGet(request, cookies, referer).ConfigureAwait(false);
			if (responseMessage == null || responseMessage.Content == null) {
				return null;
			}

			return await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
		}

		internal static async Task<string> UrlPostToContent(string request, Dictionary<string, string> postData = null, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HttpResponseMessage responseMessage = await UrlPost(request, postData, cookies, referer).ConfigureAwait(false);
			if (responseMessage == null || responseMessage.Content == null) {
				return null;
			}

			return await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
		}

		internal static async Task<string> UrlGetToTitle(string request, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HtmlDocument htmlDocument = await UrlGetToHtmlDocument(request, cookies, referer).ConfigureAwait(false);
			if (htmlDocument == null) {
				return null;
			}

			HtmlNode htmlNode = htmlDocument.DocumentNode.SelectSingleNode("//head/title");
			if (htmlNode == null) {
				return null;
			}

			return htmlNode.InnerText;
		}

		internal static async Task<HtmlDocument> UrlGetToHtmlDocument(string request, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			HttpResponseMessage httpResponse = await UrlGet(request, cookies, referer).ConfigureAwait(false);
			if (httpResponse == null) {
				return null;
			}

			return await HttpResponseToHtmlDocument(httpResponse).ConfigureAwait(false);
		}

		internal static async Task<JArray> UrlGetToJArray(string request, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, cookies, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			JArray jArray;

			try {
				jArray = JArray.Parse(content);
			} catch (Exception e) {
				Logging.LogGenericException("WebBrowser", e);
				return null;
			}

			return jArray;
		}

		internal static async Task<JObject> UrlGetToJObject(string request, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, cookies, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			JObject jObject;

			try {
				jObject = JObject.Parse(content);
			} catch (Exception e) {
				Logging.LogGenericException("WebBrowser", e);
				return null;
			}

			return jObject;
		}

		internal static async Task<XmlDocument> UrlGetToXML(string request, Dictionary<string, string> cookies = null, string referer = null) {
			if (string.IsNullOrEmpty(request)) {
				return null;
			}

			string content = await UrlGetToContent(request, cookies, referer).ConfigureAwait(false);
			if (string.IsNullOrEmpty(content)) {
				return null;
			}

			XmlDocument xmlDocument = new XmlDocument();

			try {
				xmlDocument.LoadXml(content);
			} catch (XmlException e) {
				Logging.LogGenericException("WebBrowser", e);
				return null;
			}

			return xmlDocument;
		}
	}
}
