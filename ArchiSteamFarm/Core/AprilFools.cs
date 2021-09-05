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
using System.Globalization;
using System.Threading;

namespace ArchiSteamFarm.Core {
	internal static class AprilFools {
		private static readonly object LockObject = new();

		// We don't care about CurrentCulture global config property, because April Fools are never initialized in this case
		private static readonly CultureInfo OriginalCulture = CultureInfo.CurrentCulture;

		private static readonly Timer Timer = new(Init);

		internal static void Init(object? state = null) {
			DateTime now = DateTime.Now;

			if ((now.Month == 4) && (now.Day == 1)) {
				try {
					CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CreateSpecificCulture("qps-Ploc");
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericDebuggingException(e);

					return;
				}

				TimeSpan aprilFoolsEnd = TimeSpan.FromDays(1) - now.TimeOfDay;

				lock (LockObject) {
					Timer.Change(aprilFoolsEnd + TimeSpan.FromMilliseconds(100), Timeout.InfiniteTimeSpan);
				}

				return;
			}

			try {
				CultureInfo.DefaultThreadCurrentCulture = CultureInfo.DefaultThreadCurrentUICulture = OriginalCulture;
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericDebuggingException(e);

				return;
			}

			// Since we already verified that it's not April Fools right now, either we're in months 1-3 before 1st April this year, or 4-12 already after the 1st April
			DateTime nextAprilFools = new(now.Month >= 4 ? now.Year + 1 : now.Year, 4, 1, 0, 0, 0, DateTimeKind.Local);

			TimeSpan aprilFoolsStart = nextAprilFools - now;

			// Timer can accept only dueTimes up to 2^32 - 2
			uint dueTime = (uint) Math.Min(uint.MaxValue - 1, (ulong) aprilFoolsStart.TotalMilliseconds + 100);

			lock (LockObject) {
				Timer.Change(dueTime, Timeout.Infinite);
			}
		}
	}
}
