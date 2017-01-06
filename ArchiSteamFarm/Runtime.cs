/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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
using System.Reflection;
using ArchiSteamFarm.Localization;
using Microsoft.Win32;

namespace ArchiSteamFarm {
	internal static class Runtime {
		internal static bool IsRunningOnMono => MonoRuntime != null;

		internal static bool IsRuntimeSupported {
			get {
				if (_IsRuntimeSupported.HasValue) {
					return _IsRuntimeSupported.Value;
				}

				if (IsRunningOnMono) {
					Version monoVersion = GetMonoVersion();
					if (monoVersion == null) {
						Program.ArchiLogger.LogNullError(nameof(monoVersion));
						return false;
					}

					Version minMonoVersion = new Version(4, 6);

					if (monoVersion >= minMonoVersion) {
						Program.ArchiLogger.LogGenericInfo(string.Format(Strings.RuntimeVersionOK, "Mono"));
						Program.ArchiLogger.LogGenericInfo(string.Format(Strings.RuntimeVersionComparison, minMonoVersion, monoVersion));
						_IsRuntimeSupported = true;
						return true;
					}

					Program.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningRuntimeVersionTooOld, "Mono"));
					Program.ArchiLogger.LogGenericWarning(string.Format(Strings.RuntimeVersionComparison, minMonoVersion, monoVersion));
					_IsRuntimeSupported = false;
					return false;
				}

				Version netVersion = GetNetVersion();
				if (netVersion == null) {
					Program.ArchiLogger.LogNullError(nameof(netVersion));
					return false;
				}

				Version minNetVersion = new Version(4, 6, 1);

				if (netVersion >= minNetVersion) {
					Program.ArchiLogger.LogGenericInfo(string.Format(Strings.RuntimeVersionOK, ".NET"));
					Program.ArchiLogger.LogGenericInfo(string.Format(Strings.RuntimeVersionComparison, minNetVersion, netVersion));
					_IsRuntimeSupported = true;
					return true;
				}

				Program.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningRuntimeVersionTooOld, ".NET"));
				Program.ArchiLogger.LogGenericWarning(string.Format(Strings.RuntimeVersionComparison, minNetVersion, netVersion));
				_IsRuntimeSupported = false;
				return false;
			}
		}

		internal static bool IsUserInteractive {
			get {
				if (_IsUserInteractive.HasValue) {
					return _IsUserInteractive.Value;
				}

				if (Environment.UserInteractive) {
					_IsUserInteractive = true;
				} else if (!IsRunningOnMono) {
					// If it's non-Mono, we can trust the result
					_IsUserInteractive = false;
				} else {
					// In Mono, Environment.UserInteractive is always false
					// There is really no reliable way for now, so assume always being interactive
					// Maybe in future I find out some awful hack or workaround that could be at least semi-reliable
					_IsUserInteractive = true;
				}

				return _IsUserInteractive.Value;
			}
		}

		internal static bool RequiresTls12Testing => IsRunningOnMono;

		private static readonly Type MonoRuntime = Type.GetType("Mono.Runtime");

		private static bool? _IsRuntimeSupported;
		private static bool? _IsUserInteractive;

		private static Version GetMonoVersion() {
			if (MonoRuntime == null) {
				Program.ArchiLogger.LogNullError(nameof(MonoRuntime));
				return null;
			}

			MethodInfo displayName = MonoRuntime.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
			if (displayName == null) {
				Program.ArchiLogger.LogNullError(nameof(displayName));
				return null;
			}

			string versionString = (string) displayName.Invoke(null, null);
			if (string.IsNullOrEmpty(versionString)) {
				Program.ArchiLogger.LogNullError(nameof(versionString));
				return null;
			}

			int index = versionString.IndexOf(' ');
			if (index <= 0) {
				Program.ArchiLogger.LogNullError(nameof(index));
				return null;
			}

			versionString = versionString.Substring(0, index);

			Version version;
			if (Version.TryParse(versionString, out version)) {
				return version;
			}

			Program.ArchiLogger.LogNullError(nameof(version));
			return null;
		}

		private static Version GetNetVersion() {
			uint release;
			using (RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32).OpenSubKey("SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full\\")) {
				if (registryKey == null) {
					Program.ArchiLogger.LogNullError(nameof(registryKey));
					return null;
				}

				object releaseObj = registryKey.GetValue("Release");
				if (releaseObj == null) {
					Program.ArchiLogger.LogNullError(nameof(releaseObj));
					return null;
				}

				if (!uint.TryParse(releaseObj.ToString(), out release) || (release == 0)) {
					Program.ArchiLogger.LogNullError(nameof(release));
					return null;
				}
			}

			// https://msdn.microsoft.com/en-us/library/hh925568(v=vs.110).aspx

			if (release >= 394802) {
				return new Version(4, 6, 2);
			}

			if (release >= 394254) {
				return new Version(4, 6, 1);
			}

			if (release >= 393295) {
				return new Version(4, 6);
			}

			if (release >= 379893) {
				return new Version(4, 5, 2);
			}

			if (release >= 378675) {
				return new Version(4, 5, 1);
			}

			return release >= 378389 ? new Version(4, 5) : null;
		}
	}
}