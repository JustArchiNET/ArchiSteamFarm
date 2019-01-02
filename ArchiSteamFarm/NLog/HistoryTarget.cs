//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
// Contact: JustArchi@JustArchi.net
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
// http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using ArchiSteamFarm.Collections;
using NLog;
using NLog.Targets;

namespace ArchiSteamFarm.NLog {
	[Target(TargetName)]
	internal sealed class HistoryTarget : TargetWithLayout {
		internal const string TargetName = "History";

		private const byte DefaultMaxCount = 20;

		internal IEnumerable<string> ArchivedMessages => HistoryQueue;

		private readonly FixedSizeConcurrentQueue<string> HistoryQueue = new FixedSizeConcurrentQueue<string>(DefaultMaxCount);

		// This is NLog config property, it must have public get() and set() capabilities
		[SuppressMessage("ReSharper", "UnusedMember.Global")]
		public byte MaxCount {
			get => HistoryQueue.MaxCount;

			set {
				if (value == 0) {
					ASF.ArchiLogger.LogNullError(nameof(value));

					return;
				}

				HistoryQueue.MaxCount = value;
			}
		}

		// This parameter-less constructor is intentionally public, as NLog uses it for creating targets
		// It must stay like this as we want to have our targets defined in our NLog.config
		[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
		public HistoryTarget() { }

		internal HistoryTarget(string name) : this() => Name = name;

		protected override void Write(LogEventInfo logEvent) {
			if (logEvent == null) {
				ASF.ArchiLogger.LogNullError(nameof(logEvent));

				return;
			}

			base.Write(logEvent);

			string message = Layout.Render(logEvent);

			HistoryQueue.Enqueue(message);
			NewHistoryEntry?.Invoke(this, new NewHistoryEntryArgs(message));
		}

		internal event EventHandler<NewHistoryEntryArgs> NewHistoryEntry;

		internal sealed class NewHistoryEntryArgs : EventArgs {
			internal readonly string Message;

			internal NewHistoryEntryArgs(string message) => Message = message ?? throw new ArgumentNullException(nameof(message));
		}
	}
}
