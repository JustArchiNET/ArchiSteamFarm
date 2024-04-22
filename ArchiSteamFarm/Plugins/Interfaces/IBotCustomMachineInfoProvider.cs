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

using System.Threading.Tasks;
using ArchiSteamFarm.Steam;
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Plugins.Interfaces;

/// <inheritdoc />
/// <summary>
///     Implementing this interface allows you to provide low-level machine info provider that will be used for providing core information during Steam login procedure.
/// </summary>
[PublicAPI]
public interface IBotCustomMachineInfoProvider : IPlugin {
	/// <summary>
	///     ASF will use this property as the <see cref="IMachineInfoProvider" /> for the specified bot.
	///     Unless you know what you're doing, you should not implement this interface yourself and let ASF decide.
	/// </summary>
	/// <remarks>This method will be called with very limited amount of bot-related data, as it's used during bot initialization. We recommend to stick with <see cref="Bot.BotName" />, <see cref="Bot.BotConfig" /> and <see cref="Bot.BotDatabase" /> exclusively.</remarks>
	/// <param name="bot">Bot object related to this callback.</param>
	/// <returns><see cref="IMachineInfoProvider" /> that will be used for the particular bot. You can return null if you want to use default implementation.</returns>
	Task<IMachineInfoProvider?> GetMachineInfoProvider(Bot bot);
}
