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

using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace ArchiSteamFarm {
	internal sealed class GlobalDatabase {
		private static readonly JsonSerializerSettings CustomSerializerSettings = new JsonSerializerSettings {
			Converters = new List<JsonConverter> {
				new IPAddressConverter(),
				new IPEndPointConverter()
			}
		};

		[JsonProperty(Required = Required.DisallowNull)]
		private uint _CellID;

		internal uint CellID {
			get {
				return _CellID;
			}
			set {
				if ((value == 0) || (_CellID == value)) {
					return;
				}

				_CellID = value;
				Save();
			}
		}

		[JsonProperty(Required = Required.DisallowNull)]
		[SuppressMessage("ReSharper", "AutoPropertyCanBeMadeGetOnly.Local")]
		internal InMemoryServerListProvider ServerListProvider { get; private set; } = new InMemoryServerListProvider();

		private readonly object FileLock = new object();

		private string FilePath;

		internal static GlobalDatabase Load(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				Logging.LogNullError(nameof(filePath));
				return null;
			}

			if (!File.Exists(filePath)) {
				return new GlobalDatabase(filePath);
			}

			GlobalDatabase globalDatabase;

			try {
				globalDatabase = JsonConvert.DeserializeObject<GlobalDatabase>(File.ReadAllText(filePath), CustomSerializerSettings);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}

			if (globalDatabase == null) {
				Logging.LogNullError(nameof(globalDatabase));
				return null;
			}

			globalDatabase.FilePath = filePath;
			return globalDatabase;
		}

		private void OnServerListUpdated(object sender, EventArgs e) => Save();

		// This constructor is used when creating new database
		private GlobalDatabase(string filePath) : this() {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
			Save();
		}

		// This constructor is used only by deserializer
		[SuppressMessage("ReSharper", "UnusedMember.Local")]
		private GlobalDatabase() {
			ServerListProvider.ServerListUpdated += OnServerListUpdated;
		}

		private void Save() {
			string json = JsonConvert.SerializeObject(this, CustomSerializerSettings);
			if (string.IsNullOrEmpty(json)) {
				Logging.LogNullError(nameof(json));
				return;
			}

			lock (FileLock) {
				for (byte i = 0; i < 5; i++) {
					try {
						File.WriteAllText(FilePath, json);
						break;
					} catch (Exception e) {
						Logging.LogGenericException(e);
					}

					Thread.Sleep(1000);
				}
			}
		}
	}
}
