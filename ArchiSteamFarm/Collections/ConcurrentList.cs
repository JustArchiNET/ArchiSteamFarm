// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2024 Łukasz "JustArchi" Domeradzki
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
using System.Collections.Generic;
using System.Text.Json.Serialization;
using JetBrains.Annotations;
using Nito.AsyncEx;

namespace ArchiSteamFarm.Collections;

public sealed class ConcurrentList<T> : IList<T>, IReadOnlyList<T> {
	[PublicAPI]
	public event EventHandler? OnModified;

	public int Count {
		get {
			using (Lock.ReaderLock()) {
				return BackingCollection.Count;
			}
		}
	}

	public bool IsReadOnly => false;

	private readonly List<T> BackingCollection;
	private readonly AsyncReaderWriterLock Lock = new();

	public T this[int index] {
		get {
			ArgumentOutOfRangeException.ThrowIfNegative(index);

			using (Lock.ReaderLock()) {
				return BackingCollection[index];
			}
		}

		set {
			ArgumentOutOfRangeException.ThrowIfNegative(index);

			using (Lock.WriterLock()) {
				BackingCollection[index] = value;
			}

			OnModified?.Invoke(this, EventArgs.Empty);
		}
	}

	[JsonConstructor]
	public ConcurrentList() => BackingCollection = [];

	public ConcurrentList(IEnumerable<T> collection) {
		ArgumentNullException.ThrowIfNull(collection);

		BackingCollection = [..collection];
	}

	public void Add(T item) {
		using (Lock.WriterLock()) {
			BackingCollection.Add(item);
		}

		OnModified?.Invoke(this, EventArgs.Empty);
	}

	public void Clear() {
		using (Lock.WriterLock()) {
			BackingCollection.Clear();
		}

		OnModified?.Invoke(this, EventArgs.Empty);
	}

	public bool Contains(T item) {
		using (Lock.ReaderLock()) {
			return BackingCollection.Contains(item);
		}
	}

	public void CopyTo(T[] array, int arrayIndex) {
		ArgumentNullException.ThrowIfNull(array);
		ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

		using (Lock.ReaderLock()) {
			BackingCollection.CopyTo(array, arrayIndex);
		}
	}

	[MustDisposeResource]
	public IEnumerator<T> GetEnumerator() => new ConcurrentEnumerator<T>(BackingCollection, Lock.ReaderLock());

	public int IndexOf(T item) {
		using (Lock.ReaderLock()) {
			return BackingCollection.IndexOf(item);
		}
	}

	public void Insert(int index, T item) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		using (Lock.WriterLock()) {
			BackingCollection.Insert(index, item);
		}

		OnModified?.Invoke(this, EventArgs.Empty);
	}

	public bool Remove(T item) {
		using (Lock.WriterLock()) {
			if (!BackingCollection.Remove(item)) {
				return false;
			}
		}

		OnModified?.Invoke(this, EventArgs.Empty);

		return true;
	}

	public void RemoveAt(int index) {
		ArgumentOutOfRangeException.ThrowIfNegative(index);

		using (Lock.WriterLock()) {
			BackingCollection.RemoveAt(index);
		}

		OnModified?.Invoke(this, EventArgs.Empty);
	}

	[MustDisposeResource]
	IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

	[PublicAPI]
	public void ReplaceWith(IEnumerable<T> collection) {
		ArgumentNullException.ThrowIfNull(collection);

		using (Lock.WriterLock()) {
			BackingCollection.Clear();
			BackingCollection.AddRange(collection);
		}

		OnModified?.Invoke(this, EventArgs.Empty);
	}
}
