//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// 
// Copyright 2015-2018 Łukasz "JustArchi" Domeradzki
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
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace ArchiSteamFarm {
	internal sealed class ConcurrentSortedHashSet<T> : IDisposable, IReadOnlyCollection<T>, ISet<T> {
		public int Count {
			get {
				CollectionSemaphore.Wait();

				try {
					return BackingCollection.Count;
				} finally {
					CollectionSemaphore.Release();
				}
			}
		}

		public bool IsReadOnly => false;

		private readonly HashSet<T> BackingCollection = new HashSet<T>();
		private readonly SemaphoreSlim CollectionSemaphore = new SemaphoreSlim(1, 1);

		public bool Add(T item) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.Add(item);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public void Clear() {
			CollectionSemaphore.Wait();

			try {
				BackingCollection.Clear();
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool Contains(T item) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.Contains(item);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public void CopyTo(T[] array, int arrayIndex) {
			CollectionSemaphore.Wait();

			try {
				BackingCollection.CopyTo(array, arrayIndex);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public void Dispose() => CollectionSemaphore.Dispose();

		public void ExceptWith(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				BackingCollection.ExceptWith(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public IEnumerator<T> GetEnumerator() => new ConcurrentEnumerator(BackingCollection, CollectionSemaphore);

		public void IntersectWith(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				BackingCollection.IntersectWith(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool IsProperSubsetOf(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.IsProperSubsetOf(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool IsProperSupersetOf(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.IsProperSupersetOf(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool IsSubsetOf(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.IsSubsetOf(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool IsSupersetOf(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.IsSupersetOf(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool Overlaps(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.Overlaps(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool Remove(T item) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.Remove(item);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public bool SetEquals(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				return BackingCollection.SetEquals(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public void SymmetricExceptWith(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				BackingCollection.SymmetricExceptWith(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		public void UnionWith(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				BackingCollection.UnionWith(other);
			} finally {
				CollectionSemaphore.Release();
			}
		}

		[SuppressMessage("ReSharper", "AssignNullToNotNullAttribute")]
		void ICollection<T>.Add(T item) => Add(item);

		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		internal void ReplaceWith(IEnumerable<T> other) {
			CollectionSemaphore.Wait();

			try {
				BackingCollection.Clear();

				foreach (T item in other) {
					BackingCollection.Add(item);
				}
			} finally {
				CollectionSemaphore.Release();
			}
		}

		private sealed class ConcurrentEnumerator : IEnumerator<T> {
			public T Current => Enumerator.Current;

			private readonly IEnumerator<T> Enumerator;
			private readonly SemaphoreSlim SemaphoreSlim;

			object IEnumerator.Current => Current;

			internal ConcurrentEnumerator(IReadOnlyCollection<T> collection, SemaphoreSlim semaphoreSlim) {
				if ((collection == null) || (semaphoreSlim == null)) {
					throw new ArgumentNullException(nameof(collection) + " || " + nameof(semaphoreSlim));
				}

				SemaphoreSlim = semaphoreSlim;
				semaphoreSlim.Wait();

				Enumerator = collection.GetEnumerator();
			}

			public void Dispose() => SemaphoreSlim.Release();
			public bool MoveNext() => Enumerator.MoveNext();
			public void Reset() => Enumerator.Reset();
		}
	}
}