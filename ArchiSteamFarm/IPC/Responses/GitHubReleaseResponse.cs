using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArchiSteamFarm.IPC.Responses {
	public sealed class GitHubReleaseResponse {
		[JsonProperty]
		private readonly IEnumerable<string> Changes;

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
			Changes = releaseResponse.MarkdownBody.Split('\n').Where(line => line.StartsWith("- ")).Select(change => ArchiSteamFarm.Utilities.MarkdownToText(change)).Where(change => !string.IsNullOrEmpty(change));
			ReleasedAt = releaseResponse.PublishedAt;
			Stable = !releaseResponse.IsPreRelease;
			Version = releaseResponse.Tag;
		}
	}
}
