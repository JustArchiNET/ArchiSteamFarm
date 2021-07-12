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

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;

namespace ArchiSteamFarm.Helpers {
	internal sealed class CrossProcessFileBasedSemaphore : ICrossProcessSemaphore, IDisposable {
		private const ushort SpinLockDelay = 1000; // In milliseconds

		private readonly string FilePath;
		private readonly SemaphoreSlim LocalSemaphore = new(1, 1);

		private FileStream? FileLock;

		internal CrossProcessFileBasedSemaphore(string name) {
			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}

			FilePath = Path.Combine(Path.GetTempPath(), SharedInfo.ASF, name);

			EnsureFileExists();
		}

		public void Dispose() {
			LocalSemaphore.Dispose();

			FileLock?.Dispose();
		}

		void ICrossProcessSemaphore.Release() {
			lock (LocalSemaphore) {
				if (FileLock == null) {
					throw new InvalidOperationException(nameof(FileLock));
				}

				FileLock.Dispose();
				FileLock = null;
			}

			LocalSemaphore.Release();
		}

		async Task ICrossProcessSemaphore.WaitAsync() {
			await LocalSemaphore.WaitAsync().ConfigureAwait(false);

			bool success = false;

			try {
				while (true) {
					try {
						lock (LocalSemaphore) {
							if (FileLock != null) {
								throw new InvalidOperationException(nameof(FileLock));
							}

							EnsureFileExists();

							FileLock = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
							success = true;

							return;
						}
					} catch (IOException) {
						await Task.Delay(SpinLockDelay).ConfigureAwait(false);
					}
				}
			} finally {
				if (!success) {
					LocalSemaphore.Release();
				}
			}
		}

		async Task<bool> ICrossProcessSemaphore.WaitAsync(int millisecondsTimeout) {
			Stopwatch stopwatch = Stopwatch.StartNew();

			if (!await LocalSemaphore.WaitAsync(millisecondsTimeout).ConfigureAwait(false)) {
				stopwatch.Stop();

				return false;
			}

			bool success = false;

			try {
				stopwatch.Stop();

				millisecondsTimeout -= (int) stopwatch.ElapsedMilliseconds;

				if (millisecondsTimeout <= 0) {
					return false;
				}

				while (true) {
					try {
						lock (LocalSemaphore) {
							if (FileLock != null) {
								throw new InvalidOperationException(nameof(FileLock));
							}

							EnsureFileExists();

							FileLock = new FileStream(FilePath, FileMode.OpenOrCreate, FileAccess.Read, FileShare.None);
							success = true;

							return true;
						}
					} catch (IOException) {
						if (millisecondsTimeout <= SpinLockDelay) {
							return false;
						}

						await Task.Delay(SpinLockDelay).ConfigureAwait(false);
						millisecondsTimeout -= SpinLockDelay;
					}
				}
			} finally {
				if (!success) {
					LocalSemaphore.Release();
				}
			}
		}

		private void EnsureFileExists() {
			if (File.Exists(FilePath)) {
				return;
			}

			string? directoryPath = Path.GetDirectoryName(FilePath);

			if (string.IsNullOrEmpty(directoryPath)) {
				ASF.ArchiLogger.LogNullError(nameof(directoryPath));

				return;
			}

			if (!Directory.Exists(directoryPath)) {
				Directory.CreateDirectory(directoryPath);

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					DirectoryInfo directoryInfo = new(directoryPath);

					try {
						DirectorySecurity directorySecurity = new(directoryPath, AccessControlSections.All);

						directoryInfo.SetAccessControl(directorySecurity);
					} catch (PrivilegeNotHeldException e) {
						// Non-critical, user might have no rights to manage the resource
						ASF.ArchiLogger.LogGenericDebuggingException(e);
					}
#if NETFRAMEWORK
				} else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
#else
				} else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
#endif
					OS.UnixSetFileAccess(directoryPath, OS.EUnixPermission.Combined777);
				}
			}

			try {
				new FileStream(FilePath, FileMode.CreateNew).Dispose();

				if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
					FileInfo fileInfo = new(FilePath);

					try {
						FileSecurity fileSecurity = new(FilePath, AccessControlSections.All);

						fileInfo.SetAccessControl(fileSecurity);
					} catch (PrivilegeNotHeldException e) {
						// Non-critical, user might have no rights to manage the resource
						ASF.ArchiLogger.LogGenericDebuggingException(e);
					}
#if NETFRAMEWORK
				} else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
#else
				} else if (RuntimeInformation.IsOSPlatform(OSPlatform.FreeBSD) || RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
#endif
					OS.UnixSetFileAccess(FilePath, OS.EUnixPermission.Combined777);
				}
			} catch (IOException) {
				// Ignored, if the file was already created in the meantime by another instance, this is fine
			}
		}
	}
}
