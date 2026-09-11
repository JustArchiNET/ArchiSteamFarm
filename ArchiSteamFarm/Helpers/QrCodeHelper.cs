// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Text;
using Net.Codecrete.QrCodeGenerator;

namespace ArchiSteamFarm.Helpers;

internal static class QrCodeHelper {
	private const byte QuietZoneModules = 2;

	internal static string GenerateAscii(string payload) {
		ArgumentException.ThrowIfNullOrEmpty(payload);

		QrCode qrCode = QrCode.EncodeText(payload, QrCode.Ecc.Low);
		int size = qrCode.Size + (QuietZoneModules * 2);

		StringBuilder result = new((size + Environment.NewLine.Length) * ((size + 1) / 2));

		for (int y = 0; y < size; y += 2) {
			for (int x = 0; x < size; x++) {
				bool top = IsDark(qrCode, x, y);
				bool bottom = ((y + 1) < size) && IsDark(qrCode, x, y + 1);

				result.Append(
					(top, bottom) switch {
						(true, true) => '█',
						(true, false) => '▀',
						(false, true) => '▄',
						_ => ' '
					}
				);
			}

			result.AppendLine();
		}

		return result.ToString();
	}

	private static bool IsDark(QrCode qrCode, int x, int y) {
		x -= QuietZoneModules;
		y -= QuietZoneModules;

		return (x >= 0) && (y >= 0) && (x < qrCode.Size) && (y < qrCode.Size) && qrCode.GetModule(x, y);
	}
}
