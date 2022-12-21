//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2022 Łukasz "JustArchi" Domeradzki
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

using System.Text.RegularExpressions;

namespace ArchiSteamFarm.Core;

internal static partial class GeneratedRegexes {
	private const string CdKeyPattern = @"^[0-9A-Z]{4,7}-[0-9A-Z]{4,7}-[0-9A-Z]{4,7}(?:(?:-[0-9A-Z]{4,7})?(?:-[0-9A-Z]{4,7}))?$";
	private const string DecimalPattern = @"[0-9\.,]+";
	private const RegexOptions DefaultOptions = RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
	private const string DigitsPattern = @"\d+";
	private const string NonAsciiPattern = @"[^\u0000-\u007F]+";

#if NETFRAMEWORK
	internal static Regex CdKey() => new(CdKeyPattern, DefaultOptions);
	internal static Regex Decimal() => new(DecimalPattern, DefaultOptions);
	internal static Regex Digits() => new(DigitsPattern, DefaultOptions);
	internal static Regex NonAscii() => new(NonAsciiPattern, DefaultOptions);
#else
	[GeneratedRegex(CdKeyPattern, DefaultOptions)]
	internal static partial Regex CdKey();

	[GeneratedRegex(DecimalPattern, DefaultOptions)]
	internal static partial Regex Decimal();

	[GeneratedRegex(DigitsPattern, DefaultOptions)]
	internal static partial Regex Digits();

	[GeneratedRegex(NonAsciiPattern, DefaultOptions)]
	internal static partial Regex NonAscii();
#endif
}
