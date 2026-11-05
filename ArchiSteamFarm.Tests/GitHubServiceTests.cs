// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Immutable;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.Web.GitHub;
using ArchiSteamFarm.Web.GitHub.Data;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[DoNotParallelize]
[TestClass]
internal sealed class GitHubServiceTests : TestBase {
	private const string Repository = "JustArchiNET/ArchiSteamFarm";

	[UsedImplicitly]
	public GitHubServiceTests(TestContext testContext) : base(testContext) => ArgumentNullException.ThrowIfNull(testContext);

	[TestCategory("Manual")]
	[TestMethod]
	[DataRow(true)]
	[DataRow(false)]
	public async Task GetLatestReleaseReturnsLatestRelease(bool stable) {
		ReleaseResponse? release = await WithWebBrowser(cancellationToken => GitHubService.GetLatestRelease(Repository, stable, cancellationToken)).ConfigureAwait(false);

		Print(release);

		if (release == null) {
			Assert.Inconclusive(Strings.FormatWarningFailedWithError(nameof(release)));
		}

		Assert.IsTrue(Version.TryParse(release.Tag, out _));
		Assert.IsGreaterThan(DateTime.MinValue, release.PublishedAt);
		Assert.IsNotEmpty(release.Assets);

		if (stable) {
			Assert.IsFalse(release.IsPreRelease);
		}
	}

	[TestCategory("Manual")]
	[TestMethod]
	public async Task GetReleaseReturnsRequestedRelease() {
		ReleaseResponse? release = await WithWebBrowser(static async cancellationToken => {
				ReleaseResponse? latestRelease = await GitHubService.GetLatestRelease(Repository, cancellationToken: cancellationToken).ConfigureAwait(false);

				return latestRelease != null ? await GitHubService.GetRelease(Repository, latestRelease.Tag, cancellationToken).ConfigureAwait(false) : null;
			}
		).ConfigureAwait(false);

		Print(release);

		if (release == null) {
			Assert.Inconclusive(Strings.FormatWarningFailedWithError(nameof(release)));
		}

		Assert.IsTrue(Version.TryParse(release.Tag, out _));
		Assert.IsGreaterThan(DateTime.MinValue, release.PublishedAt);
		Assert.IsNotEmpty(release.Assets);
		Assert.IsFalse(release.IsPreRelease);
	}

	[TestCategory("Manual")]
	[TestMethod]
	public async Task GetReleasesReturnsRequestedCount() {
		ImmutableList<ReleaseResponse>? releases = await WithWebBrowser(static cancellationToken => GitHubService.GetReleases(Repository, 2, cancellationToken)).ConfigureAwait(false);

		Print(releases);

		if (releases == null) {
			Assert.Inconclusive(Strings.FormatWarningFailedWithError(nameof(releases)));
		}

		Print(releases);

		Assert.HasCount(2, releases);

		foreach (ReleaseResponse release in releases) {
			Assert.IsTrue(Version.TryParse(release.Tag, out _));
			Assert.IsGreaterThan(DateTime.MinValue, release.PublishedAt);
			Assert.IsNotEmpty(release.Assets);
		}
	}
}
#pragma warning restore CA1812 // False positive, the class is used during MSTest
