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
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Collections;

public sealed class ConcurrentHashSet<T> : IReadOnlyCollection<T>, ISet<T> where T : notnull {
	public event EventHandler? OnModified;

	public int Count => BackingCollection.Count;
	public bool IsReadOnly => false;

	private readonly ConcurrentDictionary<T, bool> BackingCollection;

	public ConcurrentHashSet() => BackingCollection = new ConcurrentDictionary<T, bool>();

	public ConcurrentHashSet(IEqualityComparer<T> comparer) {
		if (comparer == null) {
			throw new ArgumentNullException(nameof(comparer));
		}

		BackingCollection = new ConcurrentDictionary<T, bool>(comparer);
	}

	public bool Add(T item) {
		if (!BackingCollection.TryAdd(item, true)) {
			return false;
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}

	public void Clear() {
		if (BackingCollection.IsEmpty) {
			return;
		}

		BackingCollection.Clear();

		OnModified?.Invoke(this, EventArgs.Empty);
	}

	public bool Contains(T item) => BackingCollection.ContainsKey(item);

	public void CopyTo(T[] array, int arrayIndex) => BackingCollection.Keys.CopyTo(array, arrayIndex);

	public void ExceptWith(IEnumerable<T> other) {
		if (other == null) {
			throw new ArgumentNullException(nameof(other));
		}

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

	public bool Remove(T item) {
		if (!BackingCollection.TryRemove(item, out _)) {
			return false;
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}

	public bool SetEquals(IEnumerable<T> other) {
		ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();

		return (otherSet.Count == Count) && otherSet.All(Contains);
	}

	public void SymmetricExceptWith(IEnumerable<T> other) {
		ISet<T> otherSet = other as ISet<T> ?? other.ToHashSet();
		HashSet<T> removed = new();

		foreach (T item in otherSet.Where(Contains)) {
			removed.Add(item);
			Remove(item);
		}

		foreach (T item in otherSet.Where(item => !removed.Contains(item))) {
			Add(item);
		}
	}

	public void UnionWith(IEnumerable<T> other) {
		if (other == null) {
			throw new ArgumentNullException(nameof(other));
		}

		foreach (T otherElement in other) {
			Add(otherElement);
		}
	}

	void ICollection<T>.Add(T item) => Add(item);

	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	[PublicAPI]
	public bool AddRange(IEnumerable<T> items) {
		bool result = false;

		foreach (T _ in items.Where(Add)) {
			result = true;
		}

		return result;
	}

	[PublicAPI]
	public bool RemoveRange(IEnumerable<T> items) {
		bool result = false;

		foreach (T _ in items.Where(Remove)) {
			result = true;
		}

		return result;
	}

	[PublicAPI]
	public bool ReplaceIfNeededWith(IReadOnlyCollection<T> other) {
		if (SetEquals(other)) {
			return false;
		}

		ReplaceWith(other);

		return true;
	}

	[PublicAPI]
	public void ReplaceWith(IEnumerable<T> other) {
		Clear();
		UnionWith(other);
	}
}
