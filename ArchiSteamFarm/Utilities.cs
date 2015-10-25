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

			ulong result = ulong.Parse(resultString, CultureInfo.InvariantCulture);
			return result;
		}

		internal static async Task<HttpResponseMessage> UrlToHttpResponse(string websiteAddress, Dictionary<string, string> cookieVariables) {
			HttpResponseMessage result = null;
			if (!string.IsNullOrEmpty(websiteAddress)) {
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
				} catch {
				}
			}
			return result;
		}

		internal static async Task<HttpResponseMessage> UrlToHttpResponse(string websiteAddress) {
			return await UrlToHttpResponse(websiteAddress, null).ConfigureAwait(false);
		}

		internal static async Task<HtmlDocument> UrlToHtmlDocument(string websiteAddress, Dictionary<string, string> cookieVariables) {
			HtmlDocument result = null;
			if (!string.IsNullOrEmpty(websiteAddress)) {
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
				} catch {
				}
			}
			return result;
		}

		internal static async Task<HtmlDocument> UrlToHtmlDocument(string websiteAddress) {
			return await UrlToHtmlDocument(websiteAddress, null).ConfigureAwait(false);
		}

		internal static async Task<bool> UrlPostRequest(string request, Dictionary<string, string> postData, Dictionary<string, string> cookieVariables, string referer = null) {
			bool result = false;
			if (!string.IsNullOrEmpty(request)) {
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
				} catch {
				}
			}
			return result;
		}
	}
}
