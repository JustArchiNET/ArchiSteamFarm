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
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers;

public sealed class ArchiCacheable<T> : IDisposable {
	private readonly TimeSpan CacheLifetime;
	private readonly SemaphoreSlim InitSemaphore = new(1, 1);
	private readonly Func<CancellationToken, Task<(bool Success, T? Result)>> ResolveFunction;

	private bool IsInitialized => InitializedAt > DateTime.MinValue;
	private bool IsPermanentCache => CacheLifetime == Timeout.InfiniteTimeSpan;
	private bool IsRecent => IsInitialized && (IsPermanentCache || (DateTime.UtcNow.Subtract(InitializedAt) < CacheLifetime));

	private DateTime InitializedAt;
	private T? InitializedValue;

	public ArchiCacheable(Func<CancellationToken, Task<(bool Success, T? Result)>> resolveFunction, TimeSpan? cacheLifetime = null) {
		ArgumentNullException.ThrowIfNull(resolveFunction);

		ResolveFunction = resolveFunction;
		CacheLifetime = cacheLifetime ?? Timeout.InfiniteTimeSpan;
	}

	public void Dispose() => InitSemaphore.Dispose();

	[PublicAPI]
	public async Task<(bool Success, T? Result)> GetValue(ECacheFallback cacheFallback = ECacheFallback.DefaultForType, CancellationToken cancellationToken = default) {
		if (!Enum.IsDefined(cacheFallback)) {
			throw new InvalidEnumArgumentException(nameof(cacheFallback), (int) cacheFallback, typeof(ECacheFallback));
		}

		if (IsRecent) {
			return (true, InitializedValue);
		}

		try {
			await InitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
		} catch (OperationCanceledException e) {
			ASF.ArchiLogger.LogGenericDebuggingException(e);

			return GetFailedValueFor(cacheFallback);
		}

		try {
			if (IsRecent) {
				return (true, InitializedValue);
			}

			(bool success, T? result) = await ResolveFunction(cancellationToken).ConfigureAwait(false);

			if (!success) {
				return GetFailedValueFor(cacheFallback, result);
			}

			InitializedValue = result;
			InitializedAt = DateTime.UtcNow;

			return (true, result);
		} catch (OperationCanceledException e) {
			ASF.ArchiLogger.LogGenericDebuggingException(e);

			return GetFailedValueFor(cacheFallback);
		} finally {
			InitSemaphore.Release();
		}
	}

	[PublicAPI]
	public async Task Reset(CancellationToken cancellationToken = default) {
		if (!IsInitialized) {
			return;
		}

		await InitSemaphore.WaitAsync(cancellationToken).ConfigureAwait(false);

		try {
			if (!IsInitialized) {
				return;
			}

			InitializedAt = DateTime.MinValue;
		} finally {
			InitSemaphore.Release();
		}
	}

	private (bool Success, T? Result) GetFailedValueFor(ECacheFallback cacheFallback, T? result = default) {
		if (!Enum.IsDefined(cacheFallback)) {
			throw new InvalidEnumArgumentException(nameof(cacheFallback), (int) cacheFallback, typeof(ECacheFallback));
		}

		return cacheFallback switch {
			ECacheFallback.DefaultForType => (false, default(T?)),
			ECacheFallback.FailedNow => (false, result),
			ECacheFallback.SuccessPreviously => (false, InitializedValue),
			_ => throw new InvalidOperationException(nameof(cacheFallback))
		};
	}
}
