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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;

namespace ArchiSteamFarm {
	internal static class Utilities {
		//private static readonly Random Random = new Random();

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		[SuppressMessage("ReSharper", "UnusedParameter.Global")]
		internal static void Forget(this object obj) { }

		internal static string GetCookieValue(this CookieContainer cookieContainer, string url, string name) {
			if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) {
				Program.ArchiLogger.LogNullError(nameof(url) + " || " + nameof(name));
				return null;
			}

			Uri uri;

			try {
				uri = new Uri(url);
			} catch (UriFormatException e) {
				Program.ArchiLogger.LogGenericException(e);
				return null;
			}

			CookieCollection cookies = cookieContainer.GetCookies(uri);
			return cookies.Count != 0 ? (from Cookie cookie in cookies where cookie.Name.Equals(name) select cookie.Value).FirstOrDefault() : null;
		}

		internal static uint GetUnixTime() => (uint) DateTimeOffset.Now.ToUnixTimeSeconds();

		/*
		internal static int RandomNext(int maxWithout) {
			if (maxWithout <= 0) {
				Program.ArchiLogger.LogNullError(nameof(maxWithout));
				return -1;
			}

			if (maxWithout == 1) {
				return 0;
			}

			lock (Random) {
				return Random.Next(maxWithout);
			}
		}
		*/

		internal static string ToHumanReadable(this TimeSpan timeSpan) {
			// It's really dirty, I'd appreciate a lot if C# offered nice TimeSpan formatting by default
			// Normally I'd use third-party library like Humanizer, but using it only for this bit is not worth it
			// Besides, ILRepack has problem merging it's library due to referencing System.Runtime

			StringBuilder result = new StringBuilder();

			if (timeSpan.Days > 0) {
				result.Append(" " + timeSpan.Days + " day");

				if (timeSpan.Days > 1) {
					result.Append('s');
				}

				result.Append(',');
			}

			if (timeSpan.Hours > 0) {
				result.Append(" " + timeSpan.Hours + " hour");
				if (timeSpan.Hours > 1) {
					result.Append('s');
				}

				result.Append(',');
			}

			if (timeSpan.Minutes > 0) {
				result.Append(" " + timeSpan.Minutes + " minute");
				if (timeSpan.Minutes > 1) {
					result.Append('s');
				}

				result.Append(',');
			}

			if (timeSpan.Seconds > 0) {
				result.Append(" " + timeSpan.Hours + " second");
				if (timeSpan.Seconds > 1) {
					result.Append('s');
				}

				result.Append(',');
			}

			if (result.Length <= 1) {
				return "";
			}

			// Get rid of initial space
			result.Remove(0, 1);

			// Get rid of last comma
			result.Length--;

			return result.ToString();
		}
	}
}