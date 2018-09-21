using Markdig;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Newtonsoft.Json;
using System;
using System.Linq;

namespace ArchiSteamFarm.IPC.Responses {
	public sealed class GitHubReleaseResponse {
		[JsonProperty]
		private readonly string Changes;

		[JsonProperty]
		private readonly DateTime ReleasedAt;

		[JsonProperty]
		private readonly bool Stable;

		[JsonProperty]
		private readonly string Version;


		internal GitHubReleaseResponse(GitHub.ReleaseResponse releaseResponse) {
			if(releaseResponse == null) {
				throw new ArgumentNullException(nameof(releaseResponse));
			}
			Changes = releaseResponse.ChangelogHTML;
			ReleasedAt = releaseResponse.PublishedAt;
			Stable = !releaseResponse.IsPreRelease;
			Version = releaseResponse.Tag;
		}
	}
}
