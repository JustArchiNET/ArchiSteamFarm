//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
//  Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
//  Contact: JustArchi@JustArchi.net
// 
//  Licensed under the Apache License, Version 2.0 (the "License");
//  you may not use this file except in compliance with the License.
//  You may obtain a copy of the License at
// 
//  http://www.apache.org/licenses/LICENSE-2.0
//      
//  Unless required by applicable law or agreed to in writing, software
//  distributed under the License is distributed on an "AS IS" BASIS,
//  WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//  See the License for the specific language governing permissions and
//  limitations under the License.

using System;
using System.Runtime;
using System.Threading;

namespace ArchiSteamFarm {
	internal static class Hacks {
		private static Timer GarbageCollectionTimer;
		private static Timer GarbageCompactionTimer;

		internal static void EnableBackgroundGC(byte period) {
			if (period == 0) {
				ASF.ArchiLogger.LogNullError(nameof(period));
				return;
			}

			if (GarbageCollectionTimer == null) {
				GarbageCollectionTimer = new Timer(
					e => GC.Collect(),
					null,
					TimeSpan.FromSeconds(period), // Delay
					TimeSpan.FromSeconds(period) // Period
				);
			}

			if (GarbageCompactionTimer == null) {
				GarbageCompactionTimer = new Timer(
					e => GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce,
					null,
					TimeSpan.FromMinutes(period), // Delay
					TimeSpan.FromMinutes(period) // Period
				);
			}
		}
	}
}