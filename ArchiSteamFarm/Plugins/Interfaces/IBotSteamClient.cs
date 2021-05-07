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

using System.Collections.Generic;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Plugins.Interfaces {
	[PublicAPI]
	public interface IBotSteamClient : IPlugin {
		/// <summary>
		///     ASF will call this method right after custom SK2 client handler initialization in order to allow you listening for callbacks in your own code.
		/// </summary>
		/// <param name="bot">Bot object related to this callback.</param>
		/// <param name="callbackManager">Callback manager object which can be used for establishing subscriptions to standard and custom callbacks.</param>
		void OnBotSteamCallbacksInit(Bot bot, CallbackManager callbackManager);

		/// <summary>
		///     ASF will call this method right after bot initialization in order to allow you hooking custom SK2 client handlers into the SteamClient.
		/// </summary>
		/// <param name="bot">Bot object related to this callback.</param>
		/// <returns>Collection of custom client handlers that are supposed to be hooked into the SteamClient by ASF. If you do not require any, just return null or empty collection.</returns>
		IReadOnlyCollection<ClientMsgHandler>? OnBotSteamHandlersInit(Bot bot);
	}
}
