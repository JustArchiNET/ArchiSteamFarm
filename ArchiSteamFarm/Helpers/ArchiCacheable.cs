//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers;

public sealed class ArchiCacheable<T> : IDisposable {
	private readonly TimeSpan CacheLifetime;
	private readonly SemaphoreSlim InitSemaphore = new(1, 1);
	private readonly Func<Task<(bool Success, T? Result)>> ResolveFunction;

	private bool IsInitialized => InitializedAt > DateTime.MinValue;
	private bool IsPermanentCache => CacheLifetime == Timeout.InfiniteTimeSpan;
	private bool IsRecent => IsPermanentCache || (DateTime.UtcNow.Subtract(InitializedAt) < CacheLifetime);

	private DateTime InitializedAt;
	private T? InitializedValue;

	public ArchiCacheable(Func<Task<(bool Success, T? Result)>> resolveFunction, TimeSpan? cacheLifetime = null) {
		ArgumentNullException.ThrowIfNull(resolveFunction);

		ResolveFunction = resolveFunction;
		CacheLifetime = cacheLifetime ?? Timeout.InfiniteTimeSpan;
	}

	public void Dispose() => InitSemaphore.Dispose();

	[PublicAPI]
	public async Task<(bool Success, T? Result)> GetValue(ECacheFallback cacheFallback = ECacheFallback.DefaultForType) {
		if (!Enum.IsDefined(cacheFallback)) {
			throw new InvalidEnumArgumentException(nameof(cacheFallback), (int) cacheFallback, typeof(ECacheFallback));
		}

		if (IsInitialized && IsRecent) {
			return (true, InitializedValue);
		}

		await InitSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			if (IsInitialized && IsRecent) {
				return (true, InitializedValue);
			}

			(bool success, T? result) = await ResolveFunction().ConfigureAwait(false);

			if (!success) {
				return cacheFallback switch {
					ECacheFallback.DefaultForType => (false, default(T?)),
					ECacheFallback.FailedNow => (false, result),
					ECacheFallback.SuccessPreviously => (false, InitializedValue),
					_ => throw new InvalidOperationException(nameof(cacheFallback))
				};
			}

			InitializedValue = result;
			InitializedAt = DateTime.UtcNow;

			return (true, result);
		} finally {
			InitSemaphore.Release();
		}
	}

	[PublicAPI]
	public async Task Reset() {
		if (!IsInitialized) {
			return;
		}

		await InitSemaphore.WaitAsync().ConfigureAwait(false);

		try {
			if (!IsInitialized) {
				return;
			}

			InitializedAt = DateTime.MinValue;
		} finally {
			InitSemaphore.Release();
		}
	}
}
