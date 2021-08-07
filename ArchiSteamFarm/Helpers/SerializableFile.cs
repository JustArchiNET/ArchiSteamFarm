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

#if NETFRAMEWORK
using File = JustArchiNET.Madness.FileMadness.File;
#else
using System.IO;
#endif
using System;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Helpers {
	public abstract class SerializableFile : IDisposable {
		private static readonly SemaphoreSlim GlobalFileSemaphore = new(1, 1);

		private readonly SemaphoreSlim FileSemaphore = new(1, 1);

		protected string? FilePath { get; set; }

		private bool ReadOnly;
		private bool SavingScheduled;

		public void Dispose() {
			Dispose(true);
			GC.SuppressFinalize(this);
		}

		protected virtual void Dispose(bool disposing) {
			if (disposing) {
				FileSemaphore.Dispose();
			}
		}

		protected async Task Save() {
			if (string.IsNullOrEmpty(FilePath)) {
				throw new InvalidOperationException(nameof(FilePath));
			}

			if (ReadOnly) {
				return;
			}

			lock (FileSemaphore) {
				if (SavingScheduled) {
					return;
				}

				SavingScheduled = true;
			}

			await FileSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				lock (FileSemaphore) {
					SavingScheduled = false;
				}

				if (ReadOnly) {
					return;
				}

				string json = JsonConvert.SerializeObject(this, Debugging.IsUserDebugging ? Formatting.Indented : Formatting.None);

				if (string.IsNullOrEmpty(json)) {
					throw new InvalidOperationException(nameof(json));
				}

				// We always want to write entire content to temporary file first, in order to never load corrupted data, also when target file doesn't exist
				string newFilePath = FilePath + ".new";

				if (File.Exists(FilePath)) {
					string currentJson = await File.ReadAllTextAsync(FilePath!).ConfigureAwait(false);

					if (json == currentJson) {
						return;
					}

					await File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

					File.Replace(newFilePath, FilePath!, null);
				} else {
					await File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

					File.Move(newFilePath, FilePath!);
				}
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			} finally {
				FileSemaphore.Release();
			}
		}

		internal async Task MakeReadOnly() {
			if (ReadOnly) {
				return;
			}

			await FileSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				if (ReadOnly) {
					return;
				}

				ReadOnly = true;
			} finally {
				FileSemaphore.Release();
			}
		}

		internal static async Task<bool> Write(string filePath, string json) {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			if (string.IsNullOrEmpty(json)) {
				throw new ArgumentNullException(nameof(json));
			}

			string newFilePath = filePath + ".new";

			await GlobalFileSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				// We always want to write entire content to temporary file first, in order to never load corrupted data, also when target file doesn't exist
				if (File.Exists(filePath)) {
					string currentJson = await File.ReadAllTextAsync(filePath).ConfigureAwait(false);

					if (json == currentJson) {
						return true;
					}

					await File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

					File.Replace(newFilePath, filePath, null);
				} else {
					await File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

					File.Move(newFilePath, filePath);
				}

				return true;
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);

				return false;
			} finally {
				GlobalFileSemaphore.Release();
			}
		}
	}
}
