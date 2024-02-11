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
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using ArchiSteamFarm.Core;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers.Json;

public static class JsonUtilities {
	[PublicAPI]
	public static readonly JsonSerializerOptions DefaultJsonSerialierOptions = CreateDefaultJsonSerializerOption();

	[PublicAPI]
	public static readonly JsonSerializerOptions IndentedJsonSerialierOptions = CreateDefaultJsonSerializerOption(true);

	[PublicAPI]
	public static JsonSerializerOptions CreateDefaultJsonSerializerOption(bool writeIndented = false) =>
		new() {
			PropertyNamingPolicy = null,
			TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { EvaluateExtraAttributes } },
			WriteIndented = writeIndented
		};

	private static void EvaluateExtraAttributes(JsonTypeInfo jsonTypeInfo) {
		ArgumentNullException.ThrowIfNull(jsonTypeInfo);

		foreach (JsonPropertyInfo property in jsonTypeInfo.Properties) {
			property.ShouldSerialize = (parent, _) => {
				bool? shouldSerialize = ShouldSerialize(parent, property);

				return shouldSerialize ?? true;
			};
		}
	}

	private static bool? ShouldSerialize(object parent, JsonPropertyInfo property) {
		ArgumentNullException.ThrowIfNull(parent);
		ArgumentNullException.ThrowIfNull(property);

		try {
			MethodInfo? shouldSerializeMethod = parent.GetType().GetMethod($"ShouldSerialize{property.Name}", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);

			if ((shouldSerializeMethod == null) || (shouldSerializeMethod.ReturnType != typeof(bool))) {
				return null;
			}

			object? shouldSerialize = shouldSerializeMethod.Invoke(parent, null);

			if (shouldSerialize is bool result) {
				return result;
			}

			return null;
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);

			return null;
		}
	}
}
