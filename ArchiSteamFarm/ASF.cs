/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.JSON;
using Newtonsoft.Json;

namespace ArchiSteamFarm {
	internal static class ASF {
		private static Timer AutoUpdatesTimer;

		internal static async Task CheckForUpdate(bool updateOverride = false) {
			string exeFile = Assembly.GetEntryAssembly().Location;
			string oldExeFile = exeFile + ".old";

			// We booted successfully so we can now remove old exe file
			if (File.Exists(oldExeFile)) {
				// It's entirely possible that old process is still running, allow at least a second before trying to remove the file
				await Task.Delay(1000).ConfigureAwait(false);

				try {
					File.Delete(oldExeFile);
				} catch (Exception e) {
					Logging.LogGenericException(e);
					Logging.LogGenericError("Could not remove old ASF binary, please remove " + oldExeFile + " manually in order for update function to work!");
				}
			}

			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Unknown) {
				return;
			}

			if ((AutoUpdatesTimer == null) && Program.GlobalConfig.AutoUpdates) {
				AutoUpdatesTimer = new Timer(
					async e => await CheckForUpdate().ConfigureAwait(false),
					null,
					TimeSpan.FromDays(1), // Delay
					TimeSpan.FromDays(1) // Period
				);

				Logging.LogGenericInfo("ASF will automatically check for new versions every 24 hours");
			}

			string releaseURL = SharedInfo.GithubReleaseURL;
			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				releaseURL += "/latest";
			}

			Logging.LogGenericInfo("Checking new version...");

			string response = await Program.WebBrowser.UrlGetToContentRetry(releaseURL).ConfigureAwait(false);
			if (string.IsNullOrEmpty(response)) {
				Logging.LogGenericWarning("Could not check latest version!");
				return;
			}

			GitHub.ReleaseResponse releaseResponse;
			if (Program.GlobalConfig.UpdateChannel == GlobalConfig.EUpdateChannel.Stable) {
				try {
					releaseResponse = JsonConvert.DeserializeObject<GitHub.ReleaseResponse>(response);
				} catch (JsonException e) {
					Logging.LogGenericException(e);
					return;
				}
			} else {
				List<GitHub.ReleaseResponse> releases;
				try {
					releases = JsonConvert.DeserializeObject<List<GitHub.ReleaseResponse>>(response);
				} catch (JsonException e) {
					Logging.LogGenericException(e);
					return;
				}

				if ((releases == null) || (releases.Count == 0)) {
					Logging.LogGenericWarning("Could not check latest version!");
					return;
				}

				releaseResponse = releases[0];
			}

			if (string.IsNullOrEmpty(releaseResponse.Tag)) {
				Logging.LogGenericWarning("Could not check latest version!");
				return;
			}

			Version newVersion = new Version(releaseResponse.Tag);

			Logging.LogGenericInfo("Local version: " + SharedInfo.Version + " | Remote version: " + newVersion);

			if (SharedInfo.Version.CompareTo(newVersion) >= 0) { // If local version is the same or newer than remote version
				return;
			}

			if (!updateOverride && !Program.GlobalConfig.AutoUpdates) {
				Logging.LogGenericInfo("New version is available!");
				Logging.LogGenericInfo("Consider updating yourself!");
				await Task.Delay(5000).ConfigureAwait(false);
				return;
			}

			if (File.Exists(oldExeFile)) {
				Logging.LogGenericWarning("Refusing to proceed with auto update as old " + oldExeFile + " binary could not be removed, please remove it manually");
				return;
			}

			// Auto update logic starts here
			if (releaseResponse.Assets == null) {
				Logging.LogGenericWarning("Could not proceed with update because that version doesn't include assets!");
				return;
			}

			string exeFileName = Path.GetFileName(exeFile);
			GitHub.ReleaseResponse.Asset binaryAsset = releaseResponse.Assets.FirstOrDefault(asset => !string.IsNullOrEmpty(asset.Name) && asset.Name.Equals(exeFileName, StringComparison.OrdinalIgnoreCase));

			if (binaryAsset == null) {
				Logging.LogGenericWarning("Could not proceed with update because there is no asset that relates to currently running binary!");
				return;
			}

			if (string.IsNullOrEmpty(binaryAsset.DownloadURL)) {
				Logging.LogGenericWarning("Could not proceed with update because download URL is empty!");
				return;
			}

			Logging.LogGenericInfo("Downloading new version...");
			Logging.LogGenericInfo("While waiting, consider donating if you appreciate the work being done :)");

			byte[] result = await Program.WebBrowser.UrlGetToBytesRetry(binaryAsset.DownloadURL).ConfigureAwait(false);
			if (result == null) {
				return;
			}

			string newExeFile = exeFile + ".new";

			// Firstly we create new exec
			try {
				File.WriteAllBytes(newExeFile, result);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return;
			}

			// Now we move current -> old
			try {
				File.Move(exeFile, oldExeFile);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				try {
					// Cleanup
					File.Delete(newExeFile);
				} catch {
					// Ignored
				}
				return;
			}

			// Now we move new -> current
			try {
				File.Move(newExeFile, exeFile);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				try {
					// Cleanup
					File.Move(oldExeFile, exeFile);
					File.Delete(newExeFile);
				} catch {
					// Ignored
				}
				return;
			}

			Logging.LogGenericInfo("Update process finished!");

			if (Program.GlobalConfig.AutoRestart) {
				Logging.LogGenericInfo("Restarting...");
				await Task.Delay(5000).ConfigureAwait(false);
				Program.Restart();
			} else {
				Logging.LogGenericInfo("Exiting...");
				await Task.Delay(5000).ConfigureAwait(false);
				Program.Exit();
			}
		}
	}
}
