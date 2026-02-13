// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Collections;

public sealed class ConcurrentHashSet<T> : IReadOnlySet<T>, ISet<T> where T : notnull {
	[PublicAPI]
	public event EventHandler? OnModified;

	public int Count => BackingCollection.Count;
	public bool IsReadOnly => false;

	private readonly ConcurrentDictionary<T, bool> BackingCollection;

	[JsonConstructor]
	public ConcurrentHashSet() => BackingCollection = new ConcurrentDictionary<T, bool>();

	public ConcurrentHashSet(IEnumerable<T> collection) {
		ArgumentNullException.ThrowIfNull(collection);

		BackingCollection = new ConcurrentDictionary<T, bool>(collection.Select(static item => new KeyValuePair<T, bool>(item, true)));
	}

	public ConcurrentHashSet(IEqualityComparer<T> comparer) {
		ArgumentNullException.ThrowIfNull(comparer);

		BackingCollection = new ConcurrentDictionary<T, bool>(comparer);
	}

	public ConcurrentHashSet(IEnumerable<T> collection, IEqualityComparer<T> comparer) {
		ArgumentNullException.ThrowIfNull(collection);
		ArgumentNullException.ThrowIfNull(comparer);

		BackingCollection = new ConcurrentDictionary<T, bool>(collection.Select(static item => new KeyValuePair<T, bool>(item, true)), comparer);
	}

	public bool Add(T item) {
		ArgumentNullException.ThrowIfNull(item);

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

	public bool Contains(T item) {
		ArgumentNullException.ThrowIfNull(item);

		return BackingCollection.ContainsKey(item);
	}

	public void CopyTo(T[] array, int arrayIndex) {
		ArgumentNullException.ThrowIfNull(array);
		ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

		BackingCollection.Keys.CopyTo(array, arrayIndex);
	}

	public void ExceptWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		RemoveRange(other);
	}

	[MustDisposeResource]
	public IEnumerator<T> GetEnumerator() => BackingCollection.Keys.GetEnumerator();

	public void IntersectWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		bool modified = false;

		foreach (T _ in this.Where(item => !otherSet.Contains(item) && BackingCollection.TryRemove(item, out _))) {
			modified = true;
		}

		if (modified) {
			OnModified?.Invoke(this, EventArgs.Empty);
		}
	}

	public bool IsProperSubsetOf(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		return (otherSet.Count > Count) && IsSubsetOf(otherSet);
	}

	public bool IsProperSupersetOf(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		return (otherSet.Count < Count) && IsSupersetOf(otherSet);
	}

	public bool IsSubsetOf(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		return this.All(otherSet.Contains);
	}

	public bool IsSupersetOf(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		return otherSet.All(Contains);
	}

	public bool Overlaps(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		return otherSet.Any(Contains);
	}

	public bool Remove(T item) {
		ArgumentNullException.ThrowIfNull(item);

		if (!BackingCollection.TryRemove(item, out _)) {
			return false;
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}

	public bool SetEquals(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		return (otherSet.Count == Count) && otherSet.All(Contains);
	}

	public void SymmetricExceptWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		IReadOnlySet<T> otherSet = other as IReadOnlySet<T> ?? other.ToHashSet();

		HashSet<T> removed = otherSet.Where(item => BackingCollection.TryRemove(item, out _)).ToHashSet();

		bool modified = removed.Count > 0;

		foreach (T _ in otherSet.Where(item => !removed.Contains(item) && BackingCollection.TryAdd(item, true))) {
			modified = true;
		}

		if (modified) {
			OnModified?.Invoke(this, EventArgs.Empty);
		}
	}

	public void UnionWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		AddRange(other);
	}

	void ICollection<T>.Add(T item) {
		ArgumentNullException.ThrowIfNull(item);

		Add(item);
	}

	[MustDisposeResource]
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	[PublicAPI]
	public bool AddRange(IEnumerable<T> items) {
		ArgumentNullException.ThrowIfNull(items);

		bool modified = false;

		foreach (T _ in items.Where(item => BackingCollection.TryAdd(item, true))) {
			modified = true;
		}

		if (modified) {
			OnModified?.Invoke(this, EventArgs.Empty);
		}

		return modified;
	}

	[PublicAPI]
	public bool RemoveRange(IEnumerable<T> items) {
		ArgumentNullException.ThrowIfNull(items);

		bool modified = false;

		foreach (T _ in items.Where(item => BackingCollection.TryRemove(item, out _))) {
			modified = true;
		}

		if (modified) {
			OnModified?.Invoke(this, EventArgs.Empty);
		}

		return modified;
	}

	[PublicAPI]
	public int RemoveWhere(Predicate<T> match) {
		ArgumentNullException.ThrowIfNull(match);

		int count = BackingCollection.Keys.Where(match.Invoke).Count(key => BackingCollection.TryRemove(key, out _));

		if (count > 0) {
			OnModified?.Invoke(this, EventArgs.Empty);
		}

		return count;
	}

	[PublicAPI]
	public bool ReplaceIfNeededWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		ICollection<T> otherCollection = other as ICollection<T> ?? other.ToHashSet();

		if (SetEquals(otherCollection)) {
			return false;
		}

		BackingCollection.Clear();

		foreach (T item in otherCollection) {
			BackingCollection.TryAdd(item, true);
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}

	[PublicAPI]
	public void ReplaceWith(IEnumerable<T> other) {
		ArgumentNullException.ThrowIfNull(other);

		ReplaceIfNeededWith(other);
	}
}
