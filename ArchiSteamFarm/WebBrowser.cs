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

			if (!responseMessage.IsSuccessStatusCode) {
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
