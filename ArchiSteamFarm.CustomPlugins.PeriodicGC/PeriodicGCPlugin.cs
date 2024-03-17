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
using System.ComponentModel.DataAnnotations;
using System.Composition;
using System.Runtime;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Plugins.Interfaces;
using JetBrains.Annotations;

namespace ArchiSteamFarm.CustomPlugins.PeriodicGC;

[Export(typeof(IPlugin))]
[UsedImplicitly]
internal sealed class PeriodicGCPlugin : IPlugin {
	private const byte GCPeriod = 60; // In seconds

	private static readonly object LockObject = new();
	private static readonly Timer PeriodicGCTimer = new(PerformGC);

	[JsonInclude]
	[Required]
	public string Name => nameof(PeriodicGCPlugin);

	[JsonInclude]
	[Required]
	public Version Version => typeof(PeriodicGCPlugin).Assembly.GetName().Version ?? throw new InvalidOperationException(nameof(Version));

	public Task OnLoaded() {
		TimeSpan timeSpan = TimeSpan.FromSeconds(GCPeriod);

		ASF.ArchiLogger.LogGenericWarning($"Periodic GC will occur every {timeSpan.ToHumanReadable()}. Please keep in mind that this plugin should be used for debugging tests only.");

		lock (LockObject) {
			PeriodicGCTimer.Change(timeSpan, timeSpan);
		}

		return Task.CompletedTask;
	}

	private static void PerformGC(object? state = null) {
		ASF.ArchiLogger.LogGenericWarning($"Performing GC, current memory: {GC.GetTotalMemory(false) / 1024} KB.");

		lock (LockObject) {
			GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
			GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
		}

		ASF.ArchiLogger.LogGenericWarning($"GC finished, current memory: {GC.GetTotalMemory(false) / 1024} KB.");
	}
}
