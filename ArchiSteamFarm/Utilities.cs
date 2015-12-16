/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015 Łukasz "JustArchi" Domeradzki
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
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Utilities {
		internal static readonly Random Random = new Random();

		internal static async Task SleepAsync(int miliseconds) {
			await Task.Delay(miliseconds).ConfigureAwait(false);
		}

		internal static ulong OnlyNumbers(string inputString) {
			if (string.IsNullOrEmpty(inputString)) {
				return 0;
			}

			string resultString = OnlyNumbersString(inputString);
			if (string.IsNullOrEmpty(resultString)) {
				return 0;
			}

			ulong result;
			if (!ulong.TryParse(resultString, out result)) {
				return 0;
			}

			return result;
		}

		internal static string OnlyNumbersString(string text) {
			if (string.IsNullOrEmpty(text)) {
				return null;
			}

			return Regex.Replace(text, @"[^\d]", "");
		}
	}
}
