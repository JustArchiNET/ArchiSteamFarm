//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Immutable;
using System.Globalization;
using System.Net;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Web;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/WWW/GitHub")]
	public sealed class GitHubController : ArchiController {
		/// <summary>
		///     Fetches the most recent GitHub release of ASF project.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("Release")]
		[ProducesResponseType(typeof(GenericResponse<GitHubReleaseResponse>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
		public async Task<ActionResult<GenericResponse>> GitHubReleaseGet() {
			GitHub.ReleaseResponse? releaseResponse = await GitHub.GetLatestRelease(false).ConfigureAwait(false);

			return releaseResponse != null ? Ok(new GenericResponse<GitHubReleaseResponse>(new GitHubReleaseResponse(releaseResponse))) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
		}

		/// <summary>
		///     Fetches specific GitHub release of ASF project. Use "latest" for latest stable release.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("Release/{version:required}")]
		[ProducesResponseType(typeof(GenericResponse<GitHubReleaseResponse>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
		public async Task<ActionResult<GenericResponse>> GitHubReleaseGet(string version) {
			if (string.IsNullOrEmpty(version)) {
				throw new ArgumentNullException(nameof(version));
			}

			GitHub.ReleaseResponse? releaseResponse;

			switch (version.ToUpperInvariant()) {
				case "LATEST":
					releaseResponse = await GitHub.GetLatestRelease().ConfigureAwait(false);

					break;
				default:
					if (!Version.TryParse(version, out Version? parsedVersion)) {
						return BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(version))));
					}

					releaseResponse = await GitHub.GetRelease(parsedVersion.ToString(4)).ConfigureAwait(false);

					break;
			}

			return releaseResponse != null ? Ok(new GenericResponse<GitHubReleaseResponse>(new GitHubReleaseResponse(releaseResponse))) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
		}

		/// <summary>
		///     Fetches history of specific GitHub page from ASF project.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("Wiki/History/{page:required}")]
		[ProducesResponseType(typeof(GenericResponse<string>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
		public async Task<ActionResult<GenericResponse>> GitHubWikiHistoryGet(string page) {
			if (string.IsNullOrEmpty(page)) {
				throw new ArgumentNullException(nameof(page));
			}

			Dictionary<string, DateTime>? revisions = await GitHub.GetWikiHistory(page).ConfigureAwait(false);

			return revisions != null ? revisions.Count > 0 ? Ok(new GenericResponse<ImmutableDictionary<string, DateTime>>(revisions.ToImmutableDictionary())) : BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(page)))) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
		}

		/// <summary>
		///     Fetches specific GitHub page of ASF project.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		///     Specifying revision is optional - when not specified, will fetch latest available. If specified revision is invalid, GitHub will automatically fetch the latest revision as well.
		/// </remarks>
		[HttpGet("Wiki/Page/{page:required}")]
		[ProducesResponseType(typeof(GenericResponse<string>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
		public async Task<ActionResult<GenericResponse>> GitHubWikiPageGet(string page, [FromQuery] string? revision = null) {
			if (string.IsNullOrEmpty(page)) {
				throw new ArgumentNullException(nameof(page));
			}

			string? html = await GitHub.GetWikiPage(page, revision).ConfigureAwait(false);

			return html switch {
				null => StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries))),
				"" => BadRequest(new GenericResponse(false, string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsInvalid, nameof(page)))),
				_ => Ok(new GenericResponse<string>(html))
			};
		}
	}
}
