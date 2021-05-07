//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2021 Łukasz "JustArchi" Domeradzki
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

using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Compatibility {
	[PublicAPI]
	public static class File {
		public static Task AppendAllTextAsync(string path, string contents) {
#if NETFRAMEWORK
			System.IO.File.AppendAllText(path, contents);

			return Task.CompletedTask;
#else
			return System.IO.File.AppendAllTextAsync(path, contents);
#endif
		}

		public static void Move(string sourceFileName, string destFileName, bool overwrite) {
#if NETFRAMEWORK
			if (overwrite && System.IO.File.Exists(destFileName)) {
				System.IO.File.Delete(destFileName);
			}

			System.IO.File.Move(sourceFileName, destFileName);
#else
			System.IO.File.Move(sourceFileName, destFileName, overwrite);
#endif
		}

		public static Task<byte[]> ReadAllBytesAsync(string path) =>
#if NETFRAMEWORK
			Task.FromResult(System.IO.File.ReadAllBytes(path));
#else
			System.IO.File.ReadAllBytesAsync(path);
#endif

		public static Task<string> ReadAllTextAsync(string path) =>
#if NETFRAMEWORK
			Task.FromResult(System.IO.File.ReadAllText(path));
#else
			System.IO.File.ReadAllTextAsync(path);
#endif

		public static Task WriteAllTextAsync(string path, string contents) {
#if NETFRAMEWORK
			System.IO.File.WriteAllText(path, contents);

			return Task.CompletedTask;
#else
			return System.IO.File.WriteAllTextAsync(path, contents);
#endif
		}
	}
}
