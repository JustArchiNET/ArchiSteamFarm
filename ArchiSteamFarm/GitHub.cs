//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using Markdig;
using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class GitHub {
		private static MarkdownDocument ExtractChangelogFromBody(string markdownText) {
			if (string.IsNullOrEmpty(markdownText)) {
				ASF.ArchiLogger.LogNullError(nameof(markdownText));
				return null;
			}

			MarkdownDocument markdownDocument = Markdown.Parse(markdownText);
			bool insideChangelog = false;

			foreach (Block block in markdownDocument.ToList()) {
				if (!insideChangelog) {
					if (block is HeadingBlock headingBlock && (headingBlock.Inline.FirstChild != null) && headingBlock.Inline.FirstChild is LiteralInline literalInline && (literalInline.Content.ToString() == "Changelog")) {
						insideChangelog = true;
					}

					markdownDocument.Remove(block);
					continue;
				}

				if (block is ThematicBreakBlock) {
					insideChangelog = false;
					markdownDocument.Remove(block);
				}
			}

			return markdownDocument;
		}

		internal static async Task<List<ReleaseResponse>> GetReleases(byte count) {
			if (count == 0) {
				ASF.ArchiLogger.LogNullError(nameof(count));
				return null;
			}

			string releaseURL = SharedInfo.GithubReleaseURL + "?per_page=" + count;

			return await GetReleasesFromURL(releaseURL).ConfigureAwait(false);
		}

		internal static async Task<ReleaseResponse> GetLatestRelease(bool stable = true) {
			string releaseURL = SharedInfo.GithubReleaseURL + (stable ? "/latest" : "?per_page=1");

			if (stable) {
				return await GetReleaseFromURL(releaseURL).ConfigureAwait(false);
			}

			List<ReleaseResponse> response = await GetReleasesFromURL(releaseURL).ConfigureAwait(false);
			if (response == null || response.Count == 0) {
				ASF.ArchiLogger.LogNullError(nameof(response));
				return null;
			}

			return response.FirstOrDefault();
		}

		internal static async Task<ReleaseResponse> GetRelease(string version) {
			if (string.IsNullOrEmpty(version)) {
				ASF.ArchiLogger.LogNullError(nameof(version));
				return null;
			}

			return await GetReleaseFromURL(SharedInfo.GithubReleaseURL + "/tags/" + version).ConfigureAwait(false);
		}

		private static async Task<ReleaseResponse> GetReleaseFromURL(string releaseURL) {
			if (string.IsNullOrEmpty(nameof(releaseURL))) {
				ASF.ArchiLogger.LogNullError(nameof(releaseURL));
				return null;
			}

			WebBrowser.ObjectResponse<ReleaseResponse> objectResponse = await Program.WebBrowser.UrlGetToJsonObject<ReleaseResponse>(releaseURL).ConfigureAwait(false);
			if (objectResponse == null) {
				ASF.ArchiLogger.LogNullError(nameof(objectResponse));
				return null;
			}

			return objectResponse.Content;
		}

		private static async Task<List<ReleaseResponse>> GetReleasesFromURL(string releaseURL) {
			if (string.IsNullOrEmpty(nameof(releaseURL))) {
				ASF.ArchiLogger.LogNullError(nameof(releaseURL));
				return null;
			}

			WebBrowser.ObjectResponse<List<ReleaseResponse>> objectResponse = await Program.WebBrowser.UrlGetToJsonObject<List<ReleaseResponse>>(releaseURL).ConfigureAwait(false);
			if ((objectResponse?.Content == null) || (objectResponse.Content.Count == 0)) {
				return null;
			}

			return objectResponse.Content;
		}


		[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
		internal sealed class ReleaseResponse {
			[JsonProperty(PropertyName = "assets", Required = Required.Always)]
			internal readonly HashSet<Asset> Assets;

			[JsonProperty(PropertyName = "tag_name", Required = Required.Always)]
			internal readonly string Tag;

			[JsonProperty(PropertyName = "body", Required = Required.Always)]
			private readonly string MarkdownBody;

			[JsonProperty(PropertyName = "published_at", Required = Required.Always)]
			internal readonly DateTime PublishedAt;

			[JsonProperty(PropertyName = "prerelease", Required = Required.Always)]
			internal readonly bool IsPreRelease;

			private MarkdownDocument _Changelog;

			private MarkdownDocument Changelog {
				get {
					if (_Changelog != null) {
						return _Changelog;
					}

					return _Changelog = ExtractChangelogFromBody(MarkdownBody);
				}
			}

			private string _ChangelogHTML;

			internal string ChangelogHTML {
				get {
					if (_ChangelogHTML != null) {
						return _ChangelogHTML;
					}

					if (Changelog == null) {
						ASF.ArchiLogger.LogNullError(nameof(Changelog));
						return null;
					}

					using (StringWriter writer = new StringWriter()) {
						HtmlRenderer renderer = new HtmlRenderer(writer);
						renderer.Render(Changelog);
						writer.Flush();

						return _ChangelogHTML = writer.ToString();
					}
				}
			}

			internal string _ChangelogPlainText;

			internal string ChangelogPlainText {
				get {
					if (_ChangelogPlainText != null) {
						return _ChangelogPlainText;
					}

					if (Changelog == null) {
						ASF.ArchiLogger.LogNullError(nameof(Changelog));
						return null;
					}

					using (StringWriter writer = new StringWriter()) {
						HtmlRenderer renderer = new HtmlRenderer(writer) {
							EnableHtmlForBlock = false,
							EnableHtmlForInline = false
						};

						renderer.Render(Changelog);
						writer.Flush();

						return _ChangelogPlainText = writer.ToString();
					}
				}
			}


			// Deserialized from JSON
			private ReleaseResponse() { }

			internal sealed class Asset {
				[JsonProperty(PropertyName = "browser_download_url", Required = Required.Always)]
				internal readonly string DownloadURL;

				[JsonProperty(PropertyName = "name", Required = Required.Always)]
				internal readonly string Name;

				[JsonProperty(PropertyName = "size", Required = Required.Always)]
				internal readonly uint Size;

				// Deserialized from JSON
				private Asset() { }
			}
		}
	}
}
