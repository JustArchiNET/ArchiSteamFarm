using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ArchiSteamFarm.IPC.Responses {
	public sealed class GitHubReleaseResponse {
		private GitHub.ReleaseResponse ReleaseResponse;

		[JsonProperty]
		private IEnumerable<string> Changes => ReleaseResponse.MarkdownBody.Split('\n').Where(line => line.StartsWith("- ")).Select(change => ArchiSteamFarm.Utilities.MarkdownToText(change)).Where(change => !string.IsNullOrEmpty(change));

		[JsonProperty]
		private string ReleasedAt => ReleaseResponse.PublishDate;

		[JsonProperty]
		private bool Stable => !ReleaseResponse.IsPreRelease;

		[JsonProperty]
		private string Version => ReleaseResponse.Tag;


		internal GitHubReleaseResponse(GitHub.ReleaseResponse releaseResponse) => ReleaseResponse = releaseResponse ?? throw new ArgumentNullException(nameof(releaseResponse));
	}
}
