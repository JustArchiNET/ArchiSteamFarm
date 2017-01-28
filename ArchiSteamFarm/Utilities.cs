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
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using ArchiSteamFarm.Localization;

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

		internal static uint GetUnixTime() => (uint) DateTimeOffset.UtcNow.ToUnixTimeSeconds();

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

		internal static bool IsValidHexadecimalString(string text) {
			if (string.IsNullOrEmpty(text)) {
				Program.ArchiLogger.LogNullError(nameof(text));
				return false;
			}

			const byte split = 16;
			for (byte i = 0; i < text.Length; i += split) {
				string textPart = string.Join("", text.Skip(i).Take(split));

				ulong ignored;
				if (!ulong.TryParse(textPart, NumberStyles.HexNumber, null, out ignored)) {
					return false;
				}
			}

			return true;
		}

		internal static IEnumerable<T> ToEnumerable<T>(this T item) {
			yield return item;
		}

		internal static string ToHumanReadable(this TimeSpan timeSpan) {
			// It's really dirty, I'd appreciate a lot if C# offered nice TimeSpan formatting by default
			// Normally I'd use third-party library like Humanizer, but using it only for this bit is not worth it
			// Besides, ILRepack has problem merging it's library due to referencing System.Runtime

			StringBuilder result = new StringBuilder();

			if (timeSpan.Days > 0) {
				result.Append((timeSpan.Days > 1 ? string.Format(Strings.TimeSpanDays, timeSpan.Days) : Strings.TimeSpanDay) + ", ");
			}

			if (timeSpan.Hours > 0) {
				result.Append((timeSpan.Hours > 1 ? string.Format(Strings.TimeSpanHours, timeSpan.Hours) : Strings.TimeSpanHour) + ", ");
			}

			if (timeSpan.Minutes > 0) {
				result.Append((timeSpan.Minutes > 1 ? string.Format(Strings.TimeSpanMinutes, timeSpan.Minutes) : Strings.TimeSpanMinute) + ", ");
			}

			if (timeSpan.Seconds > 0) {
				result.Append((timeSpan.Seconds > 1 ? string.Format(Strings.TimeSpanSeconds, timeSpan.Seconds) : Strings.TimeSpanSecond) + ", ");
			}

			if (result.Length == 0) {
				return string.Format(Strings.TimeSpanSeconds, 0);
			}

			// Get rid of last comma + space
			result.Length -= 2;

			return result.ToString();
		}
	}
}