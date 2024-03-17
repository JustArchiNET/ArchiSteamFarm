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
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers.Json;

/// <inheritdoc />
/// <summary>
///     TODO: This class exists purely because STJ can't deserialize Guid in other formats than default, at least for now
///     https://github.com/dotnet/runtime/issues/30692
/// </summary>
[PublicAPI]
public sealed class GuidJsonConverter : JsonConverter<Guid> {
	public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) {
		if (reader.TryGetGuid(out Guid result)) {
			// Great, we can work with it
			return result;
		}

		try {
			// Try again using more flexible implementation, sigh
			return Guid.Parse(reader.GetString()!);
		} catch {
			// Throw JsonException instead, which will be converted into standard message by STJ
			throw new JsonException();
		}
	}

	public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) {
		ArgumentNullException.ThrowIfNull(writer);

		writer.WriteStringValue(value.ToString());
	}
}
