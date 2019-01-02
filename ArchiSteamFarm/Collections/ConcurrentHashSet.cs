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

using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace ArchiSteamFarm.Collections {
	internal sealed class ConcurrentHashSet<T> : IReadOnlyCollection<T>, ISet<T> {
		public int Count => BackingCollection.Count;
		public bool IsReadOnly => false;

		private readonly ConcurrentDictionary<T, bool> BackingCollection = new ConcurrentDictionary<T, bool>();

		public bool Add(T item) => BackingCollection.TryAdd(item, true);
		public void Clear() => BackingCollection.Clear();
		public bool Contains(T item) => BackingCollection.ContainsKey(item);
		public void CopyTo(T[] array, int arrayIndex) => BackingCollection.Keys.CopyTo(array, arrayIndex);

		public void ExceptWith(IEnumerable<T> other) {
			foreach (T item in other) {
				Remove(item);
			}
		}

		public IEnumerator<T> GetEnumerator() => BackingCollection.Keys.GetEnumerator();

		public void IntersectWith(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			foreach (T item in this.Where(item => !otherSet.Contains(item))) {
				Remove(item);
			}
		}

		public bool IsProperSubsetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return (otherSet.Count > Count) && IsSubsetOf(otherSet);
		}

		public bool IsProperSupersetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return (otherSet.Count < Count) && IsSupersetOf(otherSet);
		}

		public bool IsSubsetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return this.All(otherSet.Contains);
		}

		public bool IsSupersetOf(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return otherSet.All(Contains);
		}

		public bool Overlaps(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return otherSet.Any(Contains);
		}

		public bool Remove(T item) => BackingCollection.TryRemove(item, out _);

		public bool SetEquals(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

			return (otherSet.Count == Count) && otherSet.All(Contains);
		}

		public void SymmetricExceptWith(IEnumerable<T> other) {
			ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();
			HashSet<T> removed = new HashSet<T>();

			foreach (T item in otherSet.Where(Contains)) {
				removed.Add(item);
				Remove(item);
			}

			foreach (T item in otherSet.Where(item => !removed.Contains(item))) {
				Add(item);
			}
		}

		public void UnionWith(IEnumerable<T> other) {
			foreach (T otherElement in other) {
				Add(otherElement);
			}
		}

		void ICollection<T>.Add(T item) => Add(item);
		IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

		// We use Count() and not Any() because we must ensure full loop pass
		internal bool AddRange(IEnumerable<T> items) => items.Count(Add) > 0;

		// We use Count() and not Any() because we must ensure full loop pass
		internal bool RemoveRange(IEnumerable<T> items) => items.Count(Remove) > 0;

		internal bool ReplaceIfNeededWith(IReadOnlyCollection<T> other) {
			if (SetEquals(other)) {
				return false;
			}

			ReplaceWith(other);

			return true;
		}

		internal void ReplaceWith(IEnumerable<T> other) {
			BackingCollection.Clear();

			foreach (T item in other) {
				BackingCollection[item] = true;
			}
		}
	}
}
