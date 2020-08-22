//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2020 Łukasz "JustArchi" Domeradzki
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
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers {
	public sealed class ArchiCacheable<T> : IDisposable where T : class {
		private readonly TimeSpan CacheLifetime;
		private readonly SemaphoreSlim InitSemaphore = new SemaphoreSlim(1, 1);
		private readonly Func<Task<(bool Success, T? Result)>> ResolveFunction;

		private bool IsInitialized => InitializedAt > DateTime.MinValue;
		private bool IsPermanentCache => CacheLifetime == Timeout.InfiniteTimeSpan;
		private bool IsRecent => IsPermanentCache || (DateTime.UtcNow.Subtract(InitializedAt) < CacheLifetime);

		private DateTime InitializedAt;
		private T? InitializedValue;

		public ArchiCacheable(Func<Task<(bool Success, T? Result)>> resolveFunction, TimeSpan? cacheLifetime = null) {
			ResolveFunction = resolveFunction ?? throw new ArgumentNullException(nameof(resolveFunction));
			CacheLifetime = cacheLifetime ?? Timeout.InfiniteTimeSpan;
		}

		public void Dispose() => InitSemaphore.Dispose();

		[PublicAPI]
		public async Task<(bool Success, T? Result)> GetValue(EFallback fallback = EFallback.DefaultForType) {
			if (!Enum.IsDefined(typeof(EFallback), fallback)) {
				throw new ArgumentNullException(nameof(fallback));
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
					switch (fallback) {
						case EFallback.DefaultForType:
							return (false, default);
						case EFallback.FailedNow:
							return (false, result);
						case EFallback.SuccessPreviously:
							return (false, InitializedValue);
						default:
							ASF.ArchiLogger.LogGenericError(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(fallback), fallback));

							goto case EFallback.DefaultForType;
					}
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
				InitializedValue = default;
			} finally {
				InitSemaphore.Release();
			}
		}

		public enum EFallback : byte {
			DefaultForType,
			FailedNow,
			SuccessPreviously
		}
	}
}
