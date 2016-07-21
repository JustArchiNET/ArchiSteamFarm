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
using System.Reflection;

namespace ArchiSteamFarm {
	internal static class Runtime {
		private static readonly Type MonoRuntime = Type.GetType("Mono.Runtime");
		private static bool IsRunningOnMono => MonoRuntime != null;

		private static bool? _IsUserInteractive;
		internal static bool IsUserInteractive {
			get {
				if (_IsUserInteractive.HasValue) {
					return _IsUserInteractive.Value;
				}

				if (Environment.UserInteractive) {
					_IsUserInteractive = true;
					return true;
				}

				// If it's non-Mono, we can trust the result
				if (!IsRunningOnMono) {
					_IsUserInteractive = false;
					return false;
				}

				// In Mono, Environment.UserInteractive is always false
				// There is really no reliable way for now, so assume always being interactive
				// Maybe in future I find out some awful hack or workaround that could be at least semi-reliable
				_IsUserInteractive = true;
				return true;
			}
		}

		// TODO: Remove me once Mono 4.6 is released
		internal static bool RequiresWorkaroundForMonoBug41701() {
			// Mono only, https://bugzilla.xamarin.com/show_bug.cgi?id=41701
			if (!IsRunningOnMono) {
				return false;
			}

			Version monoVersion = GetMonoVersion();
			if (monoVersion == null) {
				return false;
			}

			return (monoVersion >= new Version(4, 4)) && (monoVersion <= new Version(4, 5, 2));
		}

		private static Version GetMonoVersion() {
			if (MonoRuntime == null) {
				Logging.LogNullError(nameof(MonoRuntime));
				return null;
			}

			MethodInfo displayName = MonoRuntime.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
			if (displayName == null) {
				Logging.LogNullError(nameof(displayName));
				return null;
			}

			string versionString = (string) displayName.Invoke(null, null);
			if (string.IsNullOrEmpty(versionString)) {
				Logging.LogNullError(nameof(versionString));
				return null;
			}

			int index = versionString.IndexOf(' ');
			if (index <= 0) {
				Logging.LogNullError(nameof(index));
				return null;
			}

			versionString = versionString.Substring(0, index);

			Version version;
			if (Version.TryParse(versionString, out version)) {
				return version;
			}

			Logging.LogNullError(nameof(version));
			return null;
		}
	}
}
