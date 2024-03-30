// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Globalization;
using System.Text.RegularExpressions;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Steam.Integration;

public static class SteamUtilities {
	[PublicAPI]
	public static string ToSteamClientLanguage(this CultureInfo cultureInfo) {
		ArgumentNullException.ThrowIfNull(cultureInfo);

		// We're doing our best here to map provided CultureInfo to language supported by Steam
		return cultureInfo.TwoLetterISOLanguageName switch {
			"bg" => "bulgarian",
			"cs" => "czech",
			"da" => "danish",
			"de" => "german",
			"es" when cultureInfo.Name is "es-419" or "es-AR" or "es-BO" or "es-BR" or "es-BZ" or "es-CL" or "es-CO" or "es-CR" or "es-CU" or "es-DO" or "es-EC" or "es-GQ" or "es-GT" or "es-HN" or "es-MX" or "es-NI" or "es-PA" or "es-PE" or "es-PH" or "es-PR" or "es-PY" or "es-SV" or "es-US" or "es-UY" or "es-VE" => "latam",
			"es" => "spanish",
			"el" => "greek",
			"fr" => "french",
			"fi" => "finnish",
			"hu" => "hungarian",
			"id" => "indonesian",
			"it" => "italian",
			"ko" => "koreana",
			"nl" => "dutch",
			"no" => "norwegian",
			"pl" => "polish",
			"pt" when cultureInfo.Name == "pt-BR" => "brazilian",
			"pt" => "portuguese",
			"ro" => "romanian",
			"ru" => "russian",
			"sv" => "swedish",
			"th" => "thai",
			"tr" => "turkish",
			"uk" => "ukrainian",
			"vi" => "vietnamese",
			"zh" when cultureInfo.Name is "zh-Hant" or "zh-HK" or "zh-MO" or "zh-TW" => "tchinese",
			"zh" => "schinese",
			_ => "english"
		};
	}

	internal static EResult? InterpretError(string errorText) {
		ArgumentException.ThrowIfNullOrEmpty(errorText);

		if ((errorText == "Timeout") || errorText.StartsWith("batched request timeout", StringComparison.Ordinal)) {
			return EResult.Timeout;
		}

		if (errorText.StartsWith("Failed to get", StringComparison.Ordinal) || errorText.StartsWith("Failed to send", StringComparison.Ordinal)) {
			return EResult.RemoteCallFailed;
		}

		string errorCodeText;

		Match match = GeneratedRegexes.InventoryEResult().Match(errorText);

		if (match.Success && match.Groups.TryGetValue("EResult", out Group? groupResult)) {
			errorCodeText = groupResult.Value;
		} else {
			int startIndex = errorText.LastIndexOf('(');

			if (startIndex < 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(errorText), errorText));

				return null;
			}

			startIndex++;

			int endIndex = errorText.IndexOf(')', startIndex + 1);

			if (endIndex < 0) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(errorText), errorText));

				return null;
			}

			errorCodeText = errorText[startIndex..endIndex];
		}

		if (!byte.TryParse(errorCodeText, out byte errorCode)) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(errorText), errorText));

			return null;
		}

		EResult result = (EResult) errorCode;

		if (!Enum.IsDefined(result)) {
			ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(errorText), errorText));

			return null;
		}

		return result;
	}
}
