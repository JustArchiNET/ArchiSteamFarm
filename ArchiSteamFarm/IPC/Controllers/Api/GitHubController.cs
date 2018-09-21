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
			if(releaseResponse == null) {
				return BadRequest(new GenericResponse<GitHubReleaseResponse>(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
			}

			return Ok(new GenericResponse<GitHubReleaseResponse>(new GitHubReleaseResponse(releaseResponse)));
		}

		[HttpGet("Releases")]
		public async Task<ActionResult<IEnumerable<GitHubReleaseResponse>>> GetReleases([FromQuery] uint? count) {
			if(count == null) {
				count = 10;
			}

			List<GitHub.ReleaseResponse> response = await GitHub.GetNLatestReleases(count.Value).ConfigureAwait(false);
			if(response == null || response.Count == 0) {
				return BadRequest(new GenericResponse<IEnumerable<GitHub.ReleaseResponse>>(false, string.Format(Strings.ErrorRequestFailedTooManyTimes, WebBrowser.MaxTries)));
			}

			IEnumerable<GitHubReleaseResponse> result = response.Select(singleResponse => new GitHubReleaseResponse(singleResponse));
			return Ok(new GenericResponse<IEnumerable<GitHubReleaseResponse>>(result));
		}
	}
}
