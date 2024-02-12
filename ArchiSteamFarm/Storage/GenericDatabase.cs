//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers;
using ArchiSteamFarm.Helpers.Json;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Storage;

public abstract class GenericDatabase : SerializableFile {
	[JsonDisallowNull]
	[JsonInclude]
	private ConcurrentDictionary<string, JsonElement> KeyValueJsonStorage { get; set; } = new();

	[PublicAPI]
	public void DeleteFromJsonStorage(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		if (!KeyValueJsonStorage.TryRemove(key, out _)) {
			return;
		}

		Utilities.InBackground(() => Save(this));
	}

	[PublicAPI]
	public JsonElement? LoadFromJsonStorage(string key) {
		ArgumentException.ThrowIfNullOrEmpty(key);

		return KeyValueJsonStorage.GetValueOrDefault(key);
	}

	[PublicAPI]
	public void SaveToJsonStorage(string key, JsonElement value) {
		ArgumentException.ThrowIfNullOrEmpty(key);
		ArgumentNullException.ThrowIfNull(value);

		if (value.ValueKind == JsonValueKind.Null) {
			DeleteFromJsonStorage(key);

			return;
		}

		if (KeyValueJsonStorage.TryGetValue(key, out JsonElement currentValue) && currentValue.Equals(value)) {
			return;
		}

		KeyValueJsonStorage[key] = value;
		Utilities.InBackground(() => Save(this));
	}

	[UsedImplicitly]
	public bool ShouldSerializeKeyValueJsonStorage() => !KeyValueJsonStorage.IsEmpty;
}
