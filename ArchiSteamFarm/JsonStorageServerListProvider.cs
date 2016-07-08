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
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Newtonsoft.Json;
using SteamKit2.Discovery;

namespace ArchiSteamFarm {
	internal sealed class JsonStorageServerListProvider : IServerListProvider {
		[JsonProperty(Required = Required.DisallowNull)]
		[SuppressMessage("ReSharper", "FieldCanBeMadeReadOnly.Local")]
		private HashSet<IPEndPoint> Servers = new HashSet<IPEndPoint>();

		internal GlobalDatabase GlobalDatabase { private get; set; }

		internal JsonStorageServerListProvider(GlobalDatabase globalDatabase) {
			if (globalDatabase == null) {
				throw new ArgumentNullException(nameof(globalDatabase));
			}

			GlobalDatabase = globalDatabase;
		}

		// This constructor is used only by deserializer
		[SuppressMessage("ReSharper", "UnusedMember.Local")]
		private JsonStorageServerListProvider() { }

		public Task<IEnumerable<IPEndPoint>> FetchServerListAsync() => Task.FromResult(Servers.Select(endpoint => endpoint));

		public Task UpdateServerListAsync(IEnumerable<IPEndPoint> endpoints) {
			if (endpoints == null) {
				Logging.LogNullError(nameof(endpoints));
				return Task.Delay(0);
			}

			Servers.Clear();
			foreach (IPEndPoint endpoint in endpoints) {
				Servers.Add(endpoint);
			}

			GlobalDatabase.Save();

			return Task.Delay(0);
		}
	}
}
