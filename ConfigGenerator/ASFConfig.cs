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

using System;
using System.Collections.Generic;
using System.IO;
using ArchiSteamFarm;
using Newtonsoft.Json;

namespace ConfigGenerator {
	internal abstract class ASFConfig {
		internal static readonly HashSet<ASFConfig> ASFConfigs = new HashSet<ASFConfig>();

		private readonly object FileLock = new object();

		internal string FilePath { get; set; }

		protected ASFConfig() {
			ASFConfigs.Add(this);
		}

		protected ASFConfig(string filePath) : this() {
			if (string.IsNullOrEmpty(filePath)) {
				throw new ArgumentNullException(nameof(filePath));
			}

			FilePath = filePath;
		}

		internal void Remove() {
			string queryPath = Path.GetFileNameWithoutExtension(FilePath);
			lock (FileLock) {
				foreach (string botFile in Directory.EnumerateFiles(SharedInfo.ConfigDirectory, queryPath + ".*")) {
					try {
						File.Delete(botFile);
					} catch (Exception e) {
						Logging.LogGenericException(e);
					}
				}
			}

			ASFConfigs.Remove(this);
		}

		internal void Rename(string botName) {
			if (string.IsNullOrEmpty(botName)) {
				Logging.LogNullError(nameof(botName));
				return;
			}

			string queryPath = Path.GetFileNameWithoutExtension(FilePath);
			lock (FileLock) {
				foreach (string botFile in Directory.EnumerateFiles(SharedInfo.ConfigDirectory, queryPath + ".*")) {
					try {
						File.Move(botFile, Path.Combine(SharedInfo.ConfigDirectory, botName + Path.GetExtension(botFile)));
					} catch (Exception e) {
						Logging.LogGenericException(e);
					}
				}

				FilePath = Path.Combine(SharedInfo.ConfigDirectory, botName + ".json");
			}
		}

		internal void Save() {
			lock (FileLock) {
				try {
					File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented));
				} catch (Exception e) {
					Logging.LogGenericException(e);
				}
			}
		}
	}
}