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
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace ArchiSteamFarm.IPC;

internal static class WebUtilities {
	internal static string? GetUnifiedName(this Type type) {
		ArgumentNullException.ThrowIfNull(type);

		return type.GenericTypeArguments.Length == 0 ? type.FullName : $"{type.Namespace}.{type.Name}{string.Join(null, type.GenericTypeArguments.Select(static innerType => $"[{innerType.GetUnifiedName()}]"))}";
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2057:TypeGetType", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	internal static Type? ParseType(string typeText) {
		ArgumentException.ThrowIfNullOrEmpty(typeText);

		Type? targetType = Type.GetType(typeText);

		if (targetType != null) {
			return targetType;
		}

		// We can try one more time by trying to smartly guess the assembly name from the namespace, this will work for custom libraries like SteamKit2
		int index = typeText.IndexOf('.', StringComparison.Ordinal);

		if ((index <= 0) || (index >= typeText.Length - 1)) {
			return null;
		}

		return Type.GetType($"{typeText},{typeText[..index]}");
	}
}
