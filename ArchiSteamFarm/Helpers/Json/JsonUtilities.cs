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
using System.Collections;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers.Json;

public static class JsonUtilities {
	[PublicAPI]
	public static readonly JsonSerializerOptions DefaultJsonSerialierOptions = new() {
		PropertyNamingPolicy = null,
		TypeInfoResolver = new DefaultJsonTypeInfoResolver { Modifiers = { EvaluateExtraAttributes } }
	};

	private static void EvaluateExtraAttributes(JsonTypeInfo jsonTypeInfo) {
		ArgumentNullException.ThrowIfNull(jsonTypeInfo);

		foreach (JsonPropertyInfo property in jsonTypeInfo.Properties) {
			property.ShouldSerialize = (_, value) => {
				if (property.AttributeProvider == null) {
					return true;
				}

				foreach (JsonDoNotSerializeAttribute attribute in property.AttributeProvider.GetCustomAttributes(typeof(JsonDoNotSerializeAttribute), true).OfType<JsonDoNotSerializeAttribute>()) {
					switch (attribute.Condition) {
						case ECondition.Always:

						// ReSharper disable once NotDisposedResource - false positive, IEnumerator is not disposable
						case ECondition.WhenNullOrEmpty when (value == null) || (value is string text && string.IsNullOrEmpty(text)) || (value is IEnumerable enumerable && !enumerable.GetEnumerator().MoveNext()):
							return false;
					}
				}

				return true;
			};
		}
	}
}
