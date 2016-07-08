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
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ArchiSteamFarm {
	internal sealed class IPEndPointConverter : JsonConverter {
		public override bool CanConvert(Type objectType) => objectType == typeof(IPEndPoint);

		public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer) {
			JObject jo = JObject.Load(reader);
			IPAddress address = jo["Address"].ToObject<IPAddress>(serializer);
			ushort port = jo["Port"].Value<ushort>();
			return new IPEndPoint(address, port);
		}

		public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer) {
			IPEndPoint ep = (IPEndPoint) value;
			writer.WriteStartObject();
			writer.WritePropertyName("Address");
			serializer.Serialize(writer, ep.Address);
			writer.WritePropertyName("Port");
			writer.WriteValue(ep.Port);
			writer.WriteEndObject();
		}
	}
}
