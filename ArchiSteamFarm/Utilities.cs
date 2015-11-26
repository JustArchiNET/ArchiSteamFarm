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
using System.Globalization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace ArchiSteamFarm {
	internal static class Utilities {
		private static readonly Random Random = new Random();

		internal static int RandomNumber(int min, int max) {
			return Random.Next(min, max + 1);
		}

		internal static byte RandomDice() {
			return (byte) RandomNumber(1, 6);
		}

		internal static async Task SleepAsync(int miliseconds) {
			await Task.Delay(miliseconds).ConfigureAwait(false);
		}

		internal static ulong OnlyNumbers(string inputString) {
			if (string.IsNullOrEmpty(inputString)) {
				return 0;
			}

			string resultString;
			try {
				Regex regexObj = new Regex(@"[^\d]");
				resultString = regexObj.Replace(inputString, "");
			} catch (ArgumentException e) {
				Logging.LogGenericException("Utilities", e);
				return 0;
			}

			return ulong.Parse(resultString, CultureInfo.InvariantCulture);
		}
	}
}
