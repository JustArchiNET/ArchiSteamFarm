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

using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace ArchiSteamFarm {
	internal sealed class ConcurrentSortedHashSet<T> : IDisposable, IReadOnlyCollection<T>, ISet<T> {
		public int Count {
			get {
				SemaphoreSlim.Wait();

				try {
					return BackingCollection.Count;
				} finally {
					SemaphoreSlim.Release();
				}
			}
		}

		public bool IsReadOnly => false;

		private readonly HashSet<T> BackingCollection = new HashSet<T>();
		private readonly SemaphoreSlim SemaphoreSlim = new SemaphoreSlim(1);

		public bool Add(T item) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.Add(item);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public void Clear() {
			SemaphoreSlim.Wait();

			try {
				BackingCollection.Clear();
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool Contains(T item) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.Contains(item);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public void CopyTo(T[] array, int arrayIndex) {
			SemaphoreSlim.Wait();

			try {
				BackingCollection.CopyTo(array, arrayIndex);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public void Dispose() => SemaphoreSlim.Dispose();

		public void ExceptWith(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				BackingCollection.ExceptWith(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public IEnumerator<T> GetEnumerator() => new ConcurrentEnumerator<T>(BackingCollection, SemaphoreSlim);

		public void IntersectWith(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				BackingCollection.IntersectWith(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool IsProperSubsetOf(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.IsProperSubsetOf(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool IsProperSupersetOf(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.IsProperSupersetOf(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool IsSubsetOf(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.IsSubsetOf(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool IsSupersetOf(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.IsSupersetOf(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool Overlaps(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.Overlaps(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool Remove(T item) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.Remove(item);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public bool SetEquals(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				return BackingCollection.SetEquals(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public void SymmetricExceptWith(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				BackingCollection.SymmetricExceptWith(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		public void UnionWith(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				BackingCollection.UnionWith(other);
			} finally {
				SemaphoreSlim.Release();
			}
		}

		void ICollection<T>.Add(T item) => Add(item);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		internal void ReplaceWith(IEnumerable<T> other) {
			SemaphoreSlim.Wait();

			try {
				BackingCollection.Clear();

				foreach (T item in other) {
					BackingCollection.Add(item);
				}
			} finally {
				SemaphoreSlim.Release();
			}
		}
	}
}