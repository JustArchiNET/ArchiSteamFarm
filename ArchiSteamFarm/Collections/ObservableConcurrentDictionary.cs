// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Collections;

public sealed class ObservableConcurrentDictionary<TKey, TValue> : IDictionary<TKey, TValue>, IReadOnlyDictionary<TKey, TValue> where TKey : notnull {
	[PublicAPI]
	public event EventHandler? OnModified;

	public int Count => BackingDictionary.Count;

	[PublicAPI]
	public bool IsEmpty => BackingDictionary.IsEmpty;

	public bool IsReadOnly => false;

	public ICollection<TKey> Keys => BackingDictionary.Keys;
	public ICollection<TValue> Values => BackingDictionary.Values;

	private readonly ConcurrentDictionary<TKey, TValue> BackingDictionary;

	IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => Keys;
	IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => Values;

	public TValue this[TKey key] {
		get {
			ArgumentNullException.ThrowIfNull(key);

			return BackingDictionary[key];
		}

		set {
			ArgumentNullException.ThrowIfNull(key);

			if (BackingDictionary.TryGetValue(key, out TValue? savedValue) && EqualityComparer<TValue>.Default.Equals(savedValue, value)) {
				return;
			}

			BackingDictionary[key] = value;
			OnModified?.Invoke(this, EventArgs.Empty);
		}
	}

	[JsonConstructor]
	public ObservableConcurrentDictionary() => BackingDictionary = new ConcurrentDictionary<TKey, TValue>();

	public ObservableConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection) {
		ArgumentNullException.ThrowIfNull(collection);

		BackingDictionary = new ConcurrentDictionary<TKey, TValue>(collection);
	}

	public ObservableConcurrentDictionary(IEqualityComparer<TKey> comparer) {
		ArgumentNullException.ThrowIfNull(comparer);

		BackingDictionary = new ConcurrentDictionary<TKey, TValue>(comparer);
	}

	public ObservableConcurrentDictionary(IEnumerable<KeyValuePair<TKey, TValue>> collection, IEqualityComparer<TKey> comparer) {
		ArgumentNullException.ThrowIfNull(collection);
		ArgumentNullException.ThrowIfNull(comparer);

		BackingDictionary = new ConcurrentDictionary<TKey, TValue>(collection, comparer);
	}

	public void Add(KeyValuePair<TKey, TValue> item) {
		(TKey key, TValue value) = item;

		Add(key, value);
	}

	public void Add(TKey key, TValue value) {
		ArgumentNullException.ThrowIfNull(key);

		if (!TryAdd(key, value)) {
			throw new ArgumentException($"An item with the same key has already been added. Key: {key}");
		}
	}

	public void Clear() {
		if (IsEmpty) {
			return;
		}

		BackingDictionary.Clear();
		OnModified?.Invoke(this, EventArgs.Empty);
	}

	public bool Contains(KeyValuePair<TKey, TValue> item) => ((ICollection<KeyValuePair<TKey, TValue>>) BackingDictionary).Contains(item);

	public bool ContainsKey(TKey key) {
		ArgumentNullException.ThrowIfNull(key);

		return BackingDictionary.ContainsKey(key);
	}

	public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex) {
		ArgumentNullException.ThrowIfNull(array);
		ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

		((ICollection<KeyValuePair<TKey, TValue>>) BackingDictionary).CopyTo(array, arrayIndex);
	}

	[MustDisposeResource]
	public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator() => BackingDictionary.GetEnumerator();

	public bool Remove(KeyValuePair<TKey, TValue> item) {
		ICollection<KeyValuePair<TKey, TValue>> collection = BackingDictionary;

		if (!collection.Remove(item)) {
			return false;
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}

	public bool Remove(TKey key) {
		ArgumentNullException.ThrowIfNull(key);

		if (!BackingDictionary.TryRemove(key, out _)) {
			return false;
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}

	public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value) {
		ArgumentNullException.ThrowIfNull(key);

		return BackingDictionary.TryGetValue(key, out value);
	}

	[MustDisposeResource]
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	[PublicAPI]
	public bool TryAdd(TKey key, TValue value) {
		ArgumentNullException.ThrowIfNull(key);

		if (!BackingDictionary.TryAdd(key, value)) {
			return false;
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}
}
