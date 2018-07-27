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
using Newtonsoft.Json;
using SteamKit2;

namespace ArchiSteamFarm {
	internal sealed class ServerRecordEndPoint {
		[JsonProperty(Required = Required.Always)]
		internal readonly string Host;

		[JsonProperty(Required = Required.Always)]
		internal readonly ushort Port;

		[JsonProperty(Required = Required.Always)]
		internal readonly ProtocolTypes ProtocolTypes;

		internal ServerRecordEndPoint(string host, ushort port, ProtocolTypes protocolTypes) {
			if (string.IsNullOrEmpty(host) || (port == 0) || (protocolTypes == 0)) {
				throw new ArgumentNullException(nameof(host) + " || " + nameof(port) + " || " + nameof(protocolTypes));
			}

			Host = host;
			Port = port;
			ProtocolTypes = protocolTypes;
		}

		private ServerRecordEndPoint() { }

		public override bool Equals(object obj) => (obj != null) && ((obj == this) || (obj is ServerRecordEndPoint serverRecord && Equals(serverRecord)));
		public override int GetHashCode() => (Host, Port, ProtocolTypes).GetHashCode();

		private bool Equals(ServerRecordEndPoint other) => string.Equals(Host, other.Host) && (Port == other.Port) && (ProtocolTypes == other.ProtocolTypes);
	}
}