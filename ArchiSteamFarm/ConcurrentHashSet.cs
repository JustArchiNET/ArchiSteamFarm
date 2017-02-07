/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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

using System.Collections;
using System.Collections.Generic;
using Nito.AsyncEx;

namespace ArchiSteamFarm {
	internal sealed class ConcurrentHashSet<T> : ICollection<T> {
		public int Count {
			get {
				using (Lock.ReaderLock()) {
					return HashSet.Count;
				}
			}
		}

		public bool IsReadOnly => false;

		private readonly HashSet<T> HashSet = new HashSet<T>();
		private readonly AsyncReaderWriterLock Lock = new AsyncReaderWriterLock();

		public void Clear() {
			using (Lock.WriterLock()) {
				HashSet.Clear();
			}
		}

		public bool Contains(T item) {
			using (Lock.ReaderLock()) {
				return HashSet.Contains(item);
			}
		}

		public void CopyTo(T[] array, int arrayIndex) {
			using (Lock.ReaderLock()) {
				HashSet.CopyTo(array, arrayIndex);
			}
		}

		public IEnumerator<T> GetEnumerator() => new ConcurrentEnumerator<T>(HashSet, Lock);

		public bool Remove(T item) {
			using (Lock.WriterLock()) {
				return HashSet.Remove(item);
			}
		}

		void ICollection<T>.Add(T item) => Add(item);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		internal void Add(T item) {
			using (Lock.WriterLock()) {
				HashSet.Add(item);
			}
		}

		internal void ClearAndTrim() {
			using (Lock.WriterLock()) {
				HashSet.Clear();
				HashSet.TrimExcess();
			}
		}

		internal bool ReplaceIfNeededWith(ICollection<T> items) {
			using (AsyncReaderWriterLock.UpgradeableReaderKey readerKey = Lock.UpgradeableReaderLock()) {
				if (HashSet.SetEquals(items)) {
					return false;
				}

				ReplaceWith(items, readerKey);
				return true;
			}
		}

		internal void ReplaceWith(IEnumerable<T> items) {
			using (Lock.WriterLock()) {
				HashSet.Clear();

				foreach (T item in items) {
					HashSet.Add(item);
				}

				HashSet.TrimExcess();
			}
		}

		private void ReplaceWith(IEnumerable<T> items, AsyncReaderWriterLock.UpgradeableReaderKey readerKey) {
			using (readerKey.Upgrade()) {
				HashSet.Clear();

				foreach (T item in items) {
					HashSet.Add(item);
				}

				HashSet.TrimExcess();
			}
		}
	}
}