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
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Helpers {
	internal sealed class CrossProcessMutexBasedSemaphore : ICrossProcessSemaphore {
		private const int SpinLockDelay = 1000; // In milliseconds

		private readonly SemaphoreSlim LocalSemaphore = new SemaphoreSlim(1, 1);

		private readonly string Name;

		private Mutex Mutex;
		private TaskCompletionSource<bool> ReleasedTask = new TaskCompletionSource<bool>();

		internal CrossProcessMutexBasedSemaphore([NotNull] string name) {
			if (string.IsNullOrEmpty(name)) {
				throw new ArgumentNullException(nameof(name));
			}

			Name = "Global\\" + name;
			ReleasedTask.SetResult(true);
		}

		public void Dispose() {
			LocalSemaphore.Dispose();

			Mutex?.Dispose();
		}

		void ICrossProcessSemaphore.Release() {
			if (Mutex == null) {
				throw new ArgumentNullException(nameof(Mutex) + " || " + nameof(ReleasedTask));
			}

			lock (LocalSemaphore) {
				if (Mutex == null) {
					throw new ArgumentNullException(nameof(Mutex) + " || " + nameof(ReleasedTask));
				}

				Mutex.Dispose();
				Mutex = null;

				ReleasedTask.SetResult(true);
			}
		}

		async Task ICrossProcessSemaphore.WaitAsync() {
			await LocalSemaphore.WaitAsync().ConfigureAwait(false);

			try {
				while (true) {
					if (Mutex != null) {
						await Task.Delay(SpinLockDelay).ConfigureAwait(false);

						continue;
					}

					// ReSharper disable once InconsistentlySynchronizedField - we do not synchronize TaskCompletionSource alone, but the whole process
					await ReleasedTask.Task.ConfigureAwait(false);

					lock (LocalSemaphore) {
						Mutex mutex = new Mutex(false, Name);

						try {
							mutex.WaitOne();
						} catch (AbandonedMutexException) {
							// Ignored, this is fine, other ASF process has been closed
						}

						ReleasedTask = new TaskCompletionSource<bool>();
						Mutex = mutex;

						return;
					}
				}
			} finally {
				LocalSemaphore.Release();
			}
		}

		async Task<bool> ICrossProcessSemaphore.WaitAsync(int millisecondsTimeout) {
			Stopwatch stopwatch = Stopwatch.StartNew();

			if (!await LocalSemaphore.WaitAsync(millisecondsTimeout).ConfigureAwait(false)) {
				stopwatch.Stop();

				return false;
			}

			try {
				stopwatch.Stop();

				millisecondsTimeout -= (int) stopwatch.ElapsedMilliseconds;

				if (millisecondsTimeout <= 0) {
					return false;
				}

				while (true) {
					if (Mutex != null) {
						if (millisecondsTimeout <= SpinLockDelay) {
							return false;
						}

						await Task.Delay(SpinLockDelay).ConfigureAwait(false);
						millisecondsTimeout -= SpinLockDelay;

						continue;
					}

					// ReSharper disable InconsistentlySynchronizedField - we do not synchronize TaskCompletionSource alone, but the whole process

					if (!ReleasedTask.Task.IsCompleted) {
						stopwatch.Restart();

						if (await Task.WhenAny(ReleasedTask.Task, Task.Delay(millisecondsTimeout)).ConfigureAwait(false) != ReleasedTask.Task) {
							return false;
						}

						stopwatch.Stop();

						millisecondsTimeout -= (int) stopwatch.ElapsedMilliseconds;

						if (millisecondsTimeout <= 0) {
							return false;
						}
					}

					// ReSharper restore InconsistentlySynchronizedField

					lock (LocalSemaphore) {
						Mutex mutex = new Mutex(false, Name);

						try {
							if (!mutex.WaitOne(millisecondsTimeout)) {
								return false;
							}
						} catch (AbandonedMutexException) {
							// Ignored, this is fine, other ASF process has been closed
						}

						ReleasedTask = new TaskCompletionSource<bool>();
						Mutex = mutex;

						return true;
					}
				}
			} finally {
				LocalSemaphore.Release();
			}
		}
	}
}
