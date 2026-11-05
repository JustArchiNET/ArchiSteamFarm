// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Collections;
using ArchiSteamFarm.Core;
using JetBrains.Annotations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests;

#pragma warning disable CA1812 // False positive, the class is used during MSTest
[TestClass]
internal sealed class ConcurrentTests : TestBase {
	[UsedImplicitly]
	public ConcurrentTests(TestContext testContext) : base(testContext) => ArgumentNullException.ThrowIfNull(testContext);

	[TestMethod]
	internal void ConcurrentDictionarySupportsWritingDuringLinq() {
		ConcurrentDictionary<ushort, bool> collection = [];

		for (byte i = 0; i < 10; i++) {
			collection.TryAdd(i, true);
		}

		Utilities.InBackground(
			() => {
				for (ushort i = 10; i < ushort.MaxValue; i++) {
					collection.TryAdd(i, true);
				}
			}, true, CancellationToken
		);

		foreach (KeyValuePair<ushort, bool> _ in collection.AsLinqThreadSafeEnumerable().OrderBy(static entry => entry.Key)) { }
	}

	[TestMethod]
	internal void ConcurrentHashSetSupportsWritingDuringLinq() {
		ConcurrentHashSet<ushort> collection = [];

		for (byte i = 0; i < 10; i++) {
			collection.Add(i);
		}

		Utilities.InBackground(
			() => {
				for (ushort i = 10; i < ushort.MaxValue; i++) {
					collection.Add(i);
				}
			}, true, CancellationToken
		);

		foreach (ushort _ in collection.AsLinqThreadSafeEnumerable().OrderBy(static entry => entry)) { }
	}

	[TestMethod]
	internal void ConcurrentListSupportsWritingDuringLinq() {
		ConcurrentList<ushort> collection = [];

		for (byte i = 0; i < 10; i++) {
			collection.Add(i);
		}

		Utilities.InBackground(
			() => {
				for (ushort i = 10; i < ushort.MaxValue; i++) {
					collection.Add(i);
				}
			}, true, CancellationToken
		);

		foreach (ushort _ in collection.AsLinqThreadSafeEnumerable().OrderBy(static entry => entry)) { }
	}

	[TestMethod]
	internal async Task InParallelWaitsForChildrenAfterCancellation() {
		using CancellationTokenSource cancellationTokenSource = new();

		TaskCompletionSource<bool> childStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
		TaskCompletionSource<bool> allowChildToFinish = new(TaskCreationOptions.RunContinuationsAsynchronously);

		Task child = waitForRelease();
		Task aggregate = Utilities.InParallel([child], cancellationTokenSource.Token);

		await childStarted.Task.WaitAsync(CancellationToken).ConfigureAwait(false);
		await cancellationTokenSource.CancelAsync().ConfigureAwait(false);

		bool returnedBeforeChildFinished = aggregate.IsCompleted;

		allowChildToFinish.SetResult(true);

		try {
			await aggregate.ConfigureAwait(false);
		} catch (OperationCanceledException) {
			// Expected after all child operations finish.
		}

		Assert.IsFalse(returnedBeforeChildFinished);
		Assert.IsTrue(aggregate.IsCanceled);

		return;

		async Task waitForRelease() {
			childStarted.SetResult(true);

			await allowChildToFinish.Task.ConfigureAwait(false);
		}
	}
}
#pragma warning restore CA1812 // False positive, the class is used during MSTest
