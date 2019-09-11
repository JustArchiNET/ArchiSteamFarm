//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2019 Łukasz "JustArchi" Domeradzki
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

using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins {
	[PublicAPI]
	public interface IBotCustomMachineID : IPlugin {
		/// <summary>
		///     ASF will call this method before logging in of each bot, allowing you to supply custom machineID for the logon attempt.
		///     If you decide to use this interface, you should ensure a fixed logic in which given bot will always use the same machine ID for subsequent logins.
		/// </summary>
		/// <param name="bot">Bot object related to this callback.</param>
		/// <returns>Custom machineID to use for the logon attempt of provided bot instance, or null for falling back to ASF default.</returns>
		Task<byte[]> OnBotCustomMachineIDQuery([NotNull] Bot bot);
	}
}
