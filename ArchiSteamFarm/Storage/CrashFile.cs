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
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Storage;

internal sealed class CrashFile : SerializableFile {
	internal DateTime LastStartup {
		get => BackingLastStartup;

		set {
			if (BackingLastStartup == value) {
				return;
			}

			BackingLastStartup = value;
			Utilities.InBackground(Save);
		}
	}

	internal byte StartupCount {
		get => BackingStartupCount;

		set {
			if (BackingStartupCount == value) {
				return;
			}

			BackingStartupCount = value;
			Utilities.InBackground(Save);
		}
	}

	[JsonInclude]
	private DateTime BackingLastStartup { get; set; }

	[JsonInclude]
	private byte BackingStartupCount { get; set; }

	private CrashFile(string filePath) : this() {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		FilePath = filePath;
	}

	[JsonConstructor]
	private CrashFile() { }

	[UsedImplicitly]
	public bool ShouldSerializeBackingLastStartup() => BackingLastStartup > DateTime.MinValue;

	[UsedImplicitly]
	public bool ShouldSerializeBackingStartupCount() => BackingStartupCount > 0;

	protected override Task Save() => Save(this);

	internal static async Task<CrashFile> CreateOrLoad(string filePath) {
		ArgumentException.ThrowIfNullOrEmpty(filePath);

		if (!File.Exists(filePath)) {
			return new CrashFile(filePath);
		}

		CrashFile? crashFile;

		try {
			string json = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

			if (string.IsNullOrEmpty(json)) {
				ASF.ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(json)));

				return new CrashFile(filePath);
			}

			crashFile = json.ToJsonObject<CrashFile>();
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return new CrashFile(filePath);
		}

		if (crashFile == null) {
			ASF.ArchiLogger.LogNullError(crashFile);

			return new CrashFile(filePath);
		}

		crashFile.FilePath = filePath;

		return crashFile;
	}
}
