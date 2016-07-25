/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace ArchiSteamFarm {
	internal sealed class ConcurrentHashSet<T> : ICollection<T>, IDisposable {
		private readonly HashSet<T> HashSet = new HashSet<T>();
		private readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();

		public bool IsReadOnly => false;
		public IEnumerator<T> GetEnumerator() => new ConcurrentEnumerator<T>(HashSet, Lock);

		public int Count {
			get {
				Lock.EnterReadLock();

				try {
					return HashSet.Count;
				} finally {
					Lock.ExitReadLock();
				}
			}
		}

		[SuppressMessage("ReSharper", "UnusedMethodReturnValue.Global")]
		public bool Add(T item) {
			Lock.EnterWriteLock();

			try {
				return HashSet.Add(item);
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public void Clear() {
			Lock.EnterWriteLock();

			try {
				HashSet.Clear();
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public void ClearAndTrim() {
			Lock.EnterWriteLock();

			try {
				HashSet.Clear();
				HashSet.TrimExcess();
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public bool Contains(T item) {
			Lock.EnterReadLock();

			try {
				return HashSet.Contains(item);
			} finally {
				Lock.ExitReadLock();
			}
		}

		public bool Remove(T item) {
			Lock.EnterWriteLock();

			try {
				return HashSet.Remove(item);
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public void Dispose() => Lock.Dispose();

		public void CopyTo(T[] array, int arrayIndex) {
			Lock.EnterReadLock();

			try {
				HashSet.CopyTo(array, arrayIndex);
			} finally {
				Lock.ExitReadLock();
			}
		}

		void ICollection<T>.Add(T item) => Add(item);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
	}
}
