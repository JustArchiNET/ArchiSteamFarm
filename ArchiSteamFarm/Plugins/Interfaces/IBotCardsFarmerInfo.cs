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

using ArchiSteamFarm.Steam;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces {
	[PublicAPI]
	public interface IBotCardsFarmerInfo : IPlugin {
		/// <summary>
		///     ASF will call this method when cards farming module is finished on given bot instance. This method will also be called when there is nothing to idle or idling is unavailable, you can use provided boolean value for determining that.
		/// </summary>
		/// <param name="bot">Bot object related to this callback.</param>
		/// <param name="farmedSomething">Bool value indicating whether the module has finished successfully, so when there was at least one card to drop, and nothing has interrupted us in the meantime.</param>
		void OnBotFarmingFinished(Bot bot, bool farmedSomething);

		/// <summary>
		///     ASF will call this method when cards farming module is started on given bot instance. The module is started only when there are valid cards to drop, so this method won't be called when there is nothing to idle.
		/// </summary>
		/// <param name="bot">Bot object related to this callback.</param>
		void OnBotFarmingStarted(Bot bot);

		/// <summary>
		///     ASF will call this method when cards farming module is stopped on given bot instance. The stop could be a result of a natural finish, or other situations (e.g. Steam networking issues, user commands).
		/// </summary>
		/// <param name="bot">Bot object related to this callback.</param>
		void OnBotFarmingStopped(Bot bot);
	}
}
