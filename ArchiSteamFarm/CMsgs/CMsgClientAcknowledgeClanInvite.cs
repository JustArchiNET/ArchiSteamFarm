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

using System.IO;
using SteamKit2;
using SteamKit2.Internal;

namespace ArchiSteamFarm.CMsgs {
	internal sealed class CMsgClientAcknowledgeClanInvite : ISteamSerializableMessage {
		internal bool AcceptInvite { private get; set; }
		internal ulong ClanID { private get; set; }

		void ISteamSerializable.Deserialize(Stream stream) {
			if (stream == null) {
				ASF.ArchiLogger.LogNullError(nameof(stream));

				return;
			}

			BinaryReader binaryReader = new BinaryReader(stream);
			ClanID = binaryReader.ReadUInt64();
			AcceptInvite = binaryReader.ReadBoolean();
		}

		EMsg ISteamSerializableMessage.GetEMsg() => EMsg.ClientAcknowledgeClanInvite;

		void ISteamSerializable.Serialize(Stream stream) {
			if (stream == null) {
				ASF.ArchiLogger.LogNullError(nameof(stream));

				return;
			}

			BinaryWriter binaryWriter = new BinaryWriter(stream);
			binaryWriter.Write(ClanID);
			binaryWriter.Write(AcceptInvite);
		}
	}
}
