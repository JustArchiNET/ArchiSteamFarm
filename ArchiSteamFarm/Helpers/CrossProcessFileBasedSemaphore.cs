// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Security.AccessControl;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm.Helpers;

internal sealed class CrossProcessFileBasedSemaphore : IAsyncDisposable, ICrossProcessSemaphore, IDisposable {
	private const byte SpinLockDelay = 200; // In milliseconds

	private readonly string FilePath;
	private readonly SemaphoreSlim LocalSemaphore = new(1, 1);

	private FileStream? FileLock;

	internal CrossProcessFileBasedSemaphore(string name) {
		ArgumentException.ThrowIfNullOrEmpty(name);

		FilePath = Path.Combine(Path.GetTempPath(), SharedInfo.ASF, name);
	}

	public void Dispose() {
		// Those are objects that are always being created if constructor doesn't throw exception
		LocalSemaphore.Dispose();

		// Those are objects that might be null and the check should be in-place
		FileLock?.Dispose();
	}

	public async ValueTask DisposeAsync() {
		// Those are objects that are always being created if constructor doesn't throw exception
		LocalSemaphore.Dispose();

		// Those are objects that might be null and the check should be in-place
		if (FileLock != null) {
			await FileLock.DisposeAsync().ConfigureAwait(false);
		}
	}

	void ICrossProcessSemaphore.Release() {
		// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
		lock (LocalSemaphore) {
			if (FileLock == null) {
				throw new InvalidOperationException(nameof(FileLock));
			}

			FileLock.Dispose();
			FileLock = null;
		}

		LocalSemaphore.Release();
	}

	async Task ICrossProcessSemaphore.WaitAsync(CancellationToken cancellationToken) {
		await LocalSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

		bool success = false;

		try {
			while (true) {
				if (!await EnsureFileExists().ConfigureAwait(false)) {
					throw new IOException(Strings.FormatWarningFailedWithError(nameof(EnsureFileExists)));
				}

				try {
					// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
					lock (LocalSemaphore) {
						if (FileLock != null) {
							throw new InvalidOperationException(nameof(FileLock));
						}

						FileLock = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
						success = true;

						return;
					}
				} catch (FileNotFoundException) {
					throw;
				} catch (IOException) {
					await Task.Delay(SpinLockDelay, cancellationToken).ConfigureAwait(false);
				}
			}
		} finally {
			if (!success) {
				LocalSemaphore.Release();
			}
		}
	}

	async Task<bool> ICrossProcessSemaphore.WaitAsync(int millisecondsTimeout, CancellationToken cancellationToken) {
		Stopwatch stopwatch = Stopwatch.StartNew();

		if (!await LocalSemaphore.WaitAsync(millisecondsTimeout, cancellationToken).ConfigureAwait(false)) {
			stopwatch.Stop();

			return false;
		}

		bool success = false;

		try {
			stopwatch.Stop();

			if (stopwatch.ElapsedMilliseconds >= millisecondsTimeout) {
				return false;
			}

			millisecondsTimeout -= (int) stopwatch.ElapsedMilliseconds;

			while (true) {
				if (!await EnsureFileExists().ConfigureAwait(false)) {
					throw new IOException(Strings.FormatWarningFailedWithError(nameof(EnsureFileExists)));
				}

				try {
					// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
					lock (LocalSemaphore) {
						if (FileLock != null) {
							throw new InvalidOperationException(nameof(FileLock));
						}

						FileLock = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None);
						success = true;

						return true;
					}
				} catch (FileNotFoundException) {
					throw;
				} catch (IOException) {
					if (millisecondsTimeout <= SpinLockDelay) {
						return false;
					}

					await Task.Delay(SpinLockDelay, cancellationToken).ConfigureAwait(false);
					millisecondsTimeout -= SpinLockDelay;
				}
			}
		} finally {
			if (!success) {
				LocalSemaphore.Release();
			}
		}
	}

	private async Task<bool> EnsureFileExists() {
		for (byte i = 0; i < 2; i++) {
			if (File.Exists(FilePath)) {
				return true;
			}

			string? directoryPath = Path.GetDirectoryName(FilePath);

			if (string.IsNullOrEmpty(directoryPath)) {
				ASF.ArchiLogger.LogNullError(directoryPath);

				return false;
			}

			if (!Directory.Exists(directoryPath)) {
				if (OperatingSystem.IsWindows()) {
					DirectoryInfo directoryInfo = new(directoryPath);

					try {
						DirectorySecurity directorySecurity = new(directoryPath, AccessControlSections.All);

						directoryInfo.Create(directorySecurity);
					} catch (UnauthorizedAccessException e) {
						// Non-critical, user might have no rights to manage the resource
						ASF.ArchiLogger.LogGenericDebuggingException(e);

						directoryInfo.Create();
					}
				} else if (OperatingSystem.IsFreeBSD() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
					// We require global access from all users, as other ASFs might need to put additional files in there
					Directory.CreateDirectory(directoryPath, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute);
				}
			}

			try {
				if (OperatingSystem.IsWindows()) {
					try {
						FileSecurity fileSecurity = new(FilePath, AccessControlSections.All);

						FileInfo fileInfo = new(FilePath);

						await fileInfo.Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, FileOptions.None, fileSecurity).DisposeAsync().ConfigureAwait(false);
					} catch (UnauthorizedAccessException e) {
						// Non-critical, user might have no rights to manage the resource
						ASF.ArchiLogger.LogGenericDebuggingException(e);

						await new FileStream(FilePath, FileMode.CreateNew, FileAccess.Write, FileShare.None).DisposeAsync().ConfigureAwait(false);
					}
				} else if (OperatingSystem.IsFreeBSD() || OperatingSystem.IsLinux() || OperatingSystem.IsMacOS()) {
					FileStreamOptions fileStreamOptions = new() {
						Mode = FileMode.CreateNew,
						Access = FileAccess.Write,
						Share = FileShare.None,

						// Since we only create and read the files, we don't need write/execute permissions on them from other instances
						UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead
					};

					await new FileStream(FilePath, fileStreamOptions).DisposeAsync().ConfigureAwait(false);
				}
			} catch (IOException e) {
				// Ignored, if the file was already created in the meantime by another instance, this is fine
				ASF.ArchiLogger.LogGenericDebuggingException(e);
			}
		}

		return false;
	}
}
