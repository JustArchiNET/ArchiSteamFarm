//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Threading.Tasks;

#if NET471
using System.Net.WebSockets;
using System.Threading;
#endif

namespace ArchiSteamFarm {
	internal static class RuntimeCompatibility {
		internal static bool IsRunningOnMono => Type.GetType("Mono.Runtime") != null;

#if NET471
		internal static async Task<WebSocketReceiveResult> ReceiveAsync(this WebSocket webSocket, byte[] buffer, CancellationToken cancellationToken) => await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken).ConfigureAwait(false);

		internal static async Task SendAsync(this WebSocket webSocket, byte[] buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken) {
			await webSocket.SendAsync(new ArraySegment<byte>(buffer), messageType, endOfMessage, cancellationToken).ConfigureAwait(false);
		}

		internal static string[] Split(this string text, char separator, StringSplitOptions options = StringSplitOptions.None) => text.Split(new[] { separator }, options);
#endif

		internal static class File {
#pragma warning disable 1998
			internal static async Task AppendAllTextAsync(string path, string contents) {
#if NET471
				System.IO.File.AppendAllText(path, contents);
#else
				await System.IO.File.AppendAllTextAsync(path, contents).ConfigureAwait(false);
#endif
			}

			internal static async Task<byte[]> ReadAllBytesAsync(string path) {
#if NET471
				return System.IO.File.ReadAllBytes(path);
#else
				return await System.IO.File.ReadAllBytesAsync(path).ConfigureAwait(false);
#endif
			}

			internal static async Task<string> ReadAllTextAsync(string path) {
#if NET471
				return System.IO.File.ReadAllText(path);
#else
				return await System.IO.File.ReadAllTextAsync(path).ConfigureAwait(false);
#endif
			}

			internal static async Task WriteAllTextAsync(string path, string contents) {
#if NET471
				System.IO.File.WriteAllText(path, contents);
#else
				await System.IO.File.WriteAllTextAsync(path, contents).ConfigureAwait(false);
#endif
			}
#pragma warning restore 1998
		}

		internal static class Path {
			internal static string GetRelativePath(string relativeTo, string path) {
#if NET471
				// This is a very silly implementation
				if (!path.StartsWith(relativeTo, StringComparison.OrdinalIgnoreCase)) {
					throw new NotImplementedException();
				}

				string result = path.Substring(relativeTo.Length);

				if ((result[0] == System.IO.Path.DirectorySeparatorChar) || (result[0] == System.IO.Path.AltDirectorySeparatorChar)) {
					return result.Substring(1);
				}

				return result;
#else
				return System.IO.Path.GetRelativePath(relativeTo, path);
#endif
			}
		}
	}
}