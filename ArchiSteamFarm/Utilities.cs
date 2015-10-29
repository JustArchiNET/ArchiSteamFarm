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
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Utilities {
		internal static ulong OnlyNumbers(string inputString) {
			if (string.IsNullOrEmpty(inputString)) {
				return 0;
			}

			string resultString;
			try {
				Regex regexObj = new Regex(@"[^\d]");
				resultString = regexObj.Replace(inputString, "");
			} catch (ArgumentException e) {
				Logging.LogGenericException("Utilities", e);
				return 0;
			}

			return ulong.Parse(resultString, CultureInfo.InvariantCulture);
		}

		internal static async Task<HttpResponseMessage> UrlToHttpResponse(string websiteAddress, Dictionary<string, string> cookieVariables = null) {
			if (string.IsNullOrEmpty(websiteAddress)) {
				return null;
			}

			HttpResponseMessage result = null;

			try {
				using (HttpClientHandler clientHandler = new HttpClientHandler { UseCookies = false }) {
					using (HttpClient client = new HttpClient(clientHandler)) {
						client.Timeout = TimeSpan.FromSeconds(10);
						HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Get, websiteAddress);
						if (cookieVariables != null) {
							StringBuilder cookie = new StringBuilder();
							foreach (KeyValuePair<string, string> cookieVariable in cookieVariables) {
								cookie.Append(cookieVariable.Key + "=" + cookieVariable.Value + ";");
							}
							requestMessage.Headers.Add("Cookie", cookie.ToString());
						}
						HttpResponseMessage responseMessage = await client.SendAsync(requestMessage).ConfigureAwait(false);
						if (responseMessage != null) {
							responseMessage.EnsureSuccessStatusCode();
							result = responseMessage;
						}
					}
				}
			} catch (Exception e) {
				Logging.LogGenericException("Utilities", e);
			}

			return result;
		}

		internal static async Task<HttpResponseMessage> UrlToHttpResponse(string websiteAddress) {
			return await UrlToHttpResponse(websiteAddress, null).ConfigureAwait(false);
		}

		internal static async Task<HtmlDocument> UrlToHtmlDocument(string websiteAddress, Dictionary<string, string> cookieVariables = null) {
			if (string.IsNullOrEmpty(websiteAddress)) {
				return null;
			}

			HtmlDocument result = null;

			try {
				HttpResponseMessage responseMessage = await UrlToHttpResponse(websiteAddress, cookieVariables).ConfigureAwait(false);
				if (responseMessage != null) {
					string source = await responseMessage.Content.ReadAsStringAsync().ConfigureAwait(false);
					if (!string.IsNullOrEmpty(source)) {
						source = WebUtility.HtmlDecode(source);
						result = new HtmlDocument();
						result.LoadHtml(source);
					}
				}
			} catch (Exception e) {
				Logging.LogGenericException("Utilities", e);
			}

			return result;
		}

		internal static async Task<bool> UrlPostRequest(string request, Dictionary<string, string> postData, Dictionary<string, string> cookieVariables = null, string referer = null) {
			if (string.IsNullOrEmpty(request) || postData == null) {
				return false;
			}

			bool result = false;

			try {
				using (HttpClientHandler clientHandler = new HttpClientHandler { UseCookies = false }) {
					using (HttpClient client = new HttpClient(clientHandler)) {
						client.Timeout = TimeSpan.FromSeconds(15);
						HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, request);
						requestMessage.Content = new FormUrlEncodedContent(postData);
						if (cookieVariables != null && cookieVariables.Count > 0) {
							StringBuilder cookie = new StringBuilder();
							foreach (KeyValuePair<string, string> cookieVariable in cookieVariables) {
								cookie.Append(cookieVariable.Key + "=" + cookieVariable.Value + ";");
							}
							requestMessage.Headers.Add("Cookie", cookie.ToString());
						}
						if (referer != null) {
							requestMessage.Headers.Referrer = new Uri(referer);
						}
						HttpResponseMessage responseMessage = await client.SendAsync(requestMessage).ConfigureAwait(false);
						if (responseMessage != null) {
							result = responseMessage.IsSuccessStatusCode;
						}
					}
				}
			} catch (Exception e) {
				Logging.LogGenericException("Utilities", e);
			}

			return result;
		}

		internal static async Task<HttpResponseMessage> UrlPostRequestWithResponse(string request, Dictionary<string, string> postData, Dictionary<string, string> cookieVariables = null, string referer = null) {
			if (string.IsNullOrEmpty(request) || postData == null) {
				return null;
			}

			HttpResponseMessage result = null;

			try {
				using (HttpClientHandler clientHandler = new HttpClientHandler { UseCookies = false }) {
					using (HttpClient client = new HttpClient(clientHandler)) {
						client.Timeout = TimeSpan.FromSeconds(10);
						HttpRequestMessage requestMessage = new HttpRequestMessage(HttpMethod.Post, request);
						requestMessage.Content = new FormUrlEncodedContent(postData);
						if (cookieVariables != null && cookieVariables.Count > 0) {
							StringBuilder cookie = new StringBuilder();
							foreach (KeyValuePair<string, string> cookieVariable in cookieVariables) {
								cookie.Append(cookieVariable.Key + "=" + cookieVariable.Value + ";");
							}
							requestMessage.Headers.Add("Cookie", cookie.ToString());
						}
						if (referer != null) {
							requestMessage.Headers.Referrer = new Uri(referer);
						}
						HttpResponseMessage responseMessage = await client.SendAsync(requestMessage).ConfigureAwait(false);
						if (responseMessage != null) {
							result = responseMessage;
						}
					}
				}
			} catch (Exception e) {
				Logging.LogGenericException("Utilities", e);
			}

			return result;
		}
	}
}
