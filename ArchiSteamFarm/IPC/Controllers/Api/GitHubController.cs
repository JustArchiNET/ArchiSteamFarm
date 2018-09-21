using ArchiSteamFarm.IPC.Responses;
using ArchiSteamFarm.Localization;
using Microsoft.AspNetCore.Mvc;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace ArchiSteamFarm.IPC.Controllers.Api {
	[ApiController]
	[Route("Api/WWW/GitHub")]
	public sealed class GitHubController : ControllerBase {
		[HttpGet("Releases/{version:required}")]
		public async Task<ActionResult<GenericResponse<GitHubReleaseResponse>>> GetRelease(string version) {
			if (string.IsNullOrEmpty(version)) {
				return BadRequest(new GenericResponse<GitHubReleaseResponse>(false, string.Format(Strings.ErrorIsEmpty, nameof(version))));
			}

			GitHub.ReleaseResponse releaseResponse = await GitHub.GetRelease(version).ConfigureAwait(false);
			if (releaseResponse == null) {
				return BadRequest(new GenericResponse<GitHubReleaseResponse>(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
			}

			return Ok(new GenericResponse<GitHubReleaseResponse>(new GitHubReleaseResponse(releaseResponse)));
		}

		[HttpGet("Releases")]
		public async Task<ActionResult<GenericResponse<IEnumerable<GitHubReleaseResponse>>>> GetReleases([FromQuery] byte count = 10) {
			if (count == 0) {
				return BadRequest(new GenericResponse<IEnumerable<GitHubReleaseResponse>>(false, string.Format(Strings.ErrorIsEmpty, nameof(count))));
			}

			List<GitHub.ReleaseResponse> response = await GitHub.GetReleases(count).ConfigureAwait(false);
			if (response == null || response.Count == 0) {
				return BadRequest(new GenericResponse<IEnumerable<GitHub.ReleaseResponse>>(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
			}

			IEnumerable<GitHubReleaseResponse> result = response.Select(singleResponse => new GitHubReleaseResponse(singleResponse));
			return Ok(new GenericResponse<IEnumerable<GitHubReleaseResponse>>(result));
		}
	}
}
