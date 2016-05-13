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
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Utilities {
		// ReSharper disable once UnusedParameter.Global
		internal static void Forget(this Task task) { }

		internal static Task ForEachAsync<T>(this IEnumerable<T> sequence, Func<T, Task> action) => action == null ? Task.FromResult(true) : Task.WhenAll(sequence.Select(action));

		internal static string GetCookieValue(this CookieContainer cookieContainer, string url, string name) {
			if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(name)) {
				return null;
			}

			CookieCollection cookies = cookieContainer.GetCookies(new Uri(url));
			return cookies.Count == 0 ? null : (from Cookie cookie in cookies where cookie.Name.Equals(name, StringComparison.Ordinal) select cookie.Value).FirstOrDefault();
		}

		internal static Task SleepAsync(int miliseconds) => miliseconds < 0 ? Task.FromResult(true) : Task.Delay(miliseconds);
	}
}
