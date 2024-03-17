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
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers;

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

	/// <summary>
	///     Implementing this method in your target class is crucial for providing supported functionality.
	///     In order to do so, it's enough to call static <see cref="Save" /> function from the parent class, providing <code>this</code> as input parameter.
	///     Afterwards, simply call your <see cref="Save" /> function whenever you need to save changes.
	///     This approach will allow JSON serializer used in the <see cref="SerializableFile" /> to properly discover all of the properties used in your class.
	///     Unfortunately, due to STJ's limitations, called by some "security", it's not possible for base class to resolve your properties automatically otherwise.
	/// </summary>
	/// <example>protected override Task Save() => Save(this);</example>
	[UsedImplicitly]
	protected abstract Task Save();

	protected static async Task Save<T>(T serializableFile) where T : SerializableFile {
		ArgumentNullException.ThrowIfNull(serializableFile);

		if (string.IsNullOrEmpty(serializableFile.FilePath)) {
			throw new InvalidOperationException(nameof(serializableFile.FilePath));
		}

		if (serializableFile.ReadOnly) {
			return;
		}

		// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
		lock (serializableFile.FileSemaphore) {
			if (serializableFile.SavingScheduled) {
				return;
			}

			serializableFile.SavingScheduled = true;
		}

		await serializableFile.FileSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			// ReSharper disable once SuspiciousLockOverSynchronizationPrimitive - this is not a mistake, we need extra synchronization, and we can re-use the semaphore object for that
			lock (serializableFile.FileSemaphore) {
				serializableFile.SavingScheduled = false;
			}

			if (serializableFile.ReadOnly) {
				return;
			}

			string json = serializableFile.ToJsonText(Debugging.IsUserDebugging);

			if (string.IsNullOrEmpty(json)) {
				throw new InvalidOperationException(nameof(json));
			}

			// We always want to write entire content to temporary file first, in order to never load corrupted data, also when target file doesn't exist
			string newFilePath = $"{serializableFile.FilePath}.new";

			if (File.Exists(serializableFile.FilePath)) {
				string currentJson = await File.ReadAllTextAsync(serializableFile.FilePath).ConfigureAwait(false);

				if (json == currentJson) {
					return;
				}

				await File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

				File.Replace(newFilePath, serializableFile.FilePath, null);
			} else {
				await File.WriteAllTextAsync(newFilePath, json).ConfigureAwait(false);

				File.Move(newFilePath, serializableFile.FilePath);
			}
		} catch (Exception e) {
			ASF.ArchiLogger.LogGenericException(e);
		} finally {
			serializableFile.FileSemaphore.Release();
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
		ArgumentException.ThrowIfNullOrEmpty(filePath);
		ArgumentException.ThrowIfNullOrEmpty(json);

		string newFilePath = $"{filePath}.new";

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
