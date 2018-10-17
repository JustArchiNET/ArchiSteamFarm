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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyCollection<string>>), 200)]
		public ActionResult<GenericResponse<IReadOnlyCollection<string>>> DirectoryGet(string directory) {
			if (string.IsNullOrEmpty(directory)) {
				ASF.ArchiLogger.LogNullError(nameof(directory));
				return BadRequest(new GenericResponse<IReadOnlyCollection<string>>(false, string.Format(Strings.ErrorIsEmpty, nameof(directory))));
			}

			string directoryPath = Path.Combine(ArchiKestrel.WebsiteDirectory, directory);
			if (!Directory.Exists(directoryPath)) {
				return BadRequest(new GenericResponse<IReadOnlyCollection<string>>(false, string.Format(Strings.ErrorIsInvalid, directory)));
			}

			string[] files;

			try {
				files = Directory.GetFiles(directoryPath);
			} catch (Exception e) {
				return BadRequest(new GenericResponse<IReadOnlyCollection<string>>(false, string.Format(Strings.ErrorParsingObject, nameof(files)) + Environment.NewLine + e));
			}

			HashSet<string> result = files.Select(Path.GetFileName).ToHashSet();
			return Ok(new GenericResponse<IReadOnlyCollection<string>>(result));
		}

		/// <summary>
		///     Fetches newest GitHub releases of ASF project.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("GitHub/Releases")]
		[ProducesResponseType(typeof(GenericResponse<IReadOnlyCollection<GitHubReleaseResponse>>), 200)]
		public async Task<ActionResult<GenericResponse<IReadOnlyCollection<GitHubReleaseResponse>>>> GitHubReleasesGet([FromQuery] byte count = 10) {
			if (count == 0) {
				return BadRequest(new GenericResponse<IReadOnlyCollection<GitHubReleaseResponse>>(false, string.Format(Strings.ErrorIsEmpty, nameof(count))));
			}

			List<GitHub.ReleaseResponse> response = await GitHub.GetReleases(count).ConfigureAwait(false);
			if ((response == null) || (response.Count == 0)) {
				return BadRequest(new GenericResponse<IReadOnlyCollection<GitHub.ReleaseResponse>>(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
			}

			List<GitHubReleaseResponse> result = response.Select(singleResponse => new GitHubReleaseResponse(singleResponse)).ToList();
			return Ok(new GenericResponse<IReadOnlyCollection<GitHubReleaseResponse>>(result));
		}

		/// <summary>
		///     Fetches specific GitHub release of ASF project.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[HttpGet("GitHub/Releases/{version:required}")]
		[ProducesResponseType(typeof(GenericResponse<GitHubReleaseResponse>), 200)]
		public async Task<ActionResult<GenericResponse<GitHubReleaseResponse>>> GitHubReleasesGet(string version) {
			if (string.IsNullOrEmpty(version)) {
				return BadRequest(new GenericResponse<GitHubReleaseResponse>(false, string.Format(Strings.ErrorIsEmpty, nameof(version))));
			}

			GitHub.ReleaseResponse releaseResponse = await GitHub.GetRelease(version).ConfigureAwait(false);
			if (releaseResponse == null) {
				return BadRequest(new GenericResponse<GitHubReleaseResponse>(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
			}

			return Ok(new GenericResponse<GitHubReleaseResponse>(new GitHubReleaseResponse(releaseResponse)));
		}

		/// <summary>
		///     Sends a HTTPS request through ASF's built-in HttpClient.
		/// </summary>
		/// <remarks>
		///     This is internal API being utilizied by our ASF-ui IPC frontend. You should not depend on existence of any /Api/WWW endpoints as they can disappear and change anytime.
		/// </remarks>
		[Consumes("application/json")]
		[HttpPost("Send")]
		[ProducesResponseType(typeof(GenericResponse<string>), 200)]
		public async Task<ActionResult<GenericResponse<string>>> SendPost([FromBody] WWWSendRequest request) {
			if (request == null) {
				ASF.ArchiLogger.LogNullError(nameof(request));
				return BadRequest(new GenericResponse<string>(false, string.Format(Strings.ErrorIsEmpty, nameof(request))));
			}

			if (string.IsNullOrEmpty(request.URL) || !Uri.TryCreate(request.URL, UriKind.Absolute, out Uri uri) || !uri.Scheme.Equals(Uri.UriSchemeHttps)) {
				return BadRequest(new GenericResponse<string>(false, string.Format(Strings.ErrorIsInvalid, nameof(request.URL))));
			}

			WebBrowser.StringResponse urlResponse = await Program.WebBrowser.UrlGetToString(request.URL).ConfigureAwait(false);
			if (urlResponse?.Content == null) {
				return BadRequest(new GenericResponse<string>(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
			}

			return Ok(new GenericResponse<string>(urlResponse.Content));
		}
	}
}
