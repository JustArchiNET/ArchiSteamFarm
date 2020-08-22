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
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Requests;
using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[Route("Api/WWW")]
	public sealed class WWWController : ArchiController {
		/// <summary>
		///     Fetches files in given directory relative to WWW root.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("Directory/{directory:required}")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyCollection<string>>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.InternalServerError)]
		public ActionResult<GenericResponse> DirectoryGet(string directory) {
			if (string.IsNullOrEmpty(directory)) {
				throw new ArgumentNullException(directory);
			}

			string directoryPath = Path.Combine(ArchiKestrel.WebsiteDirectory, directory);

			if (!Directory.Exists(directoryPath)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsInvalid, directory)));
			}

			string[] files;

			try {
				files = Directory.GetFiles(directoryPath);
			} catch (Exception e) {
				return StatusCode((int) HttpStatusCode.InternalServerError, new GenericResponse(false, string.Format(Strings.ErrorParsingObject, nameof(files)) + Environment.NewLine + e));
			}

			HashSet<string> result = files.Select(Path.GetFileName).Where(fileName => !string.IsNullOrEmpty(fileName)).ToHashSet()!;

			return Ok(new GenericResponse<IReadOnlyCollection<string>>(result));
		}

		/// <summary>
		///     Fetches the most recent GitHub release of ASF project.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("GitHub/Release")]
		[ProducesResponseType(typeof(GenericResponse<GitHubReleaseResponse>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
		public async Task<ActionResult<GenericResponse>> GitHubReleaseGet() {
			GitHub.ReleaseResponse? releaseResponse = await GitHub.GetLatestRelease(false).ConfigureAwait(false);

			return releaseResponse != null ? Ok(new GenericResponse<GitHubReleaseResponse>(new GitHubReleaseResponse(releaseResponse))) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
		}

		/// <summary>
		///     Fetches specific GitHub release of ASF project. Use "latest" for latest stable release.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("GitHub/Release/{version:required}")]
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
						return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsInvalid, nameof(version))));
					}

					releaseResponse = await GitHub.GetRelease(parsedVersion.ToString(4)).ConfigureAwait(false);

					break;
			}

			return releaseResponse != null ? Ok(new GenericResponse<GitHubReleaseResponse>(new GitHubReleaseResponse(releaseResponse))) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
		}

		/// <summary>
		///     Sends a HTTPS request through ASF's built-in HttpClient.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[Consumes("application/json")]
		[HttpPost("Send")]
		[ProducesResponseType(typeof(GenericResponse<string>), (int) HttpStatusCode.OK)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.BadRequest)]
		[ProducesResponseType(typeof(GenericResponse), (int) HttpStatusCode.ServiceUnavailable)]
		public async Task<ActionResult<GenericResponse>> SendPost([FromBody] WWWSendRequest request) {
			if ((request == null) || (ASF.WebBrowser == null)) {
				throw new ArgumentNullException(nameof(request) + " || " + nameof(ASF.WebBrowser));
			}

			if (string.IsNullOrEmpty(request.URL) || !Uri.TryCreate(request.URL, UriKind.Absolute, out Uri? uri) || !uri.Scheme.Equals(Uri.UriSchemeHttps)) {
				return BadRequest(new GenericResponse(false, string.Format(Strings.ErrorIsInvalid, nameof(request.URL))));
			}

			WebBrowser.StringResponse? urlResponse = await ASF.WebBrowser.UrlGetToString(request.URL!).ConfigureAwait(false);

			return urlResponse?.Content != null ? Ok(new GenericResponse<string>(urlResponse.Content)) : StatusCode((int) HttpStatusCode.ServiceUnavailable, new GenericResponse(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
		}
	}
}
