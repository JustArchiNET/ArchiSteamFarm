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

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace ArchiSteamFarm.Collections {
	internal sealed class ConcurrentList<T> : IList<T>, IReadOnlyList<T> {
		public bool IsReadOnly => false;

		internal int Count {
			get {
				Lock.EnterReadLock();

				try {
					return BackingCollection.Count;
				} finally {
					Lock.ExitReadLock();
				}
			}
		}

		private readonly List<T> BackingCollection = new List<T>();
		private readonly ReaderWriterLockSlim Lock = new ReaderWriterLockSlim();

		int ICollection<T>.Count => Count;
		int IReadOnlyCollection<T>.Count => Count;

		public T this[int index] {
			get {
				Lock.EnterReadLock();

				try {
					return BackingCollection[index];
				} finally {
					Lock.ExitReadLock();
				}
			}

			set {
				Lock.EnterWriteLock();

				try {
					BackingCollection[index] = value;
				} finally {
					Lock.ExitWriteLock();
				}
			}
		}

		public void Add(T item) {
			Lock.EnterWriteLock();

			try {
				BackingCollection.Add(item);
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public void Clear() {
			Lock.EnterWriteLock();

			try {
				BackingCollection.Clear();
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public bool Contains(T item) {
			Lock.EnterReadLock();

			try {
				return BackingCollection.Contains(item);
			} finally {
				Lock.ExitReadLock();
			}
		}

		public void CopyTo(T[] array, int arrayIndex) {
			Lock.EnterReadLock();

			try {
				BackingCollection.CopyTo(array, arrayIndex);
			} finally {
				Lock.ExitReadLock();
			}
		}

		[JetBrains.Annotations.NotNull]
		[SuppressMessage("ReSharper", "AnnotationRedundancyInHierarchy")]
		public IEnumerator<T> GetEnumerator() => new ConcurrentEnumerator<T>(BackingCollection, Lock);

		public int IndexOf(T item) {
			Lock.EnterReadLock();

			try {
				return BackingCollection.IndexOf(item);
			} finally {
				Lock.ExitReadLock();
			}
		}

		public void Insert(int index, T item) {
			Lock.EnterWriteLock();

			try {
				BackingCollection.Insert(index, item);
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public bool Remove(T item) {
			Lock.EnterWriteLock();

			try {
				return BackingCollection.Remove(item);
			} finally {
				Lock.ExitWriteLock();
			}
		}

		public void RemoveAt(int index) {
			Lock.EnterWriteLock();

			try {
				BackingCollection.RemoveAt(index);
			} finally {
				Lock.ExitWriteLock();
			}
		}

		[JetBrains.Annotations.NotNull]
		[SuppressMessage("ReSharper", "AnnotationRedundancyInHierarchy")]
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		internal void ReplaceWith([JetBrains.Annotations.NotNull] IEnumerable<T> collection) {
			Lock.EnterWriteLock();

			try {
				BackingCollection.Clear();
				BackingCollection.AddRange(collection);
			} finally {
				Lock.ExitWriteLock();
			}
		}
	}
}
