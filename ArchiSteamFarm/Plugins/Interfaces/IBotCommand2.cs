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
using ArchiSteamFarm.Storage;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces;

/// <inheritdoc />
/// <summary>
///     Implementing this interface allows you to react to each command received by ASF, in particular, implementing your own custom commands.
/// </summary>
/// <remarks>This is used for commands, irrelevant of their origin (IPC, console, Steam). If you want to grab non-command Steam messages, check <see cref="IBotMessage" /> interface instead.</remarks>
[PublicAPI]
public interface IBotCommand2 : IPlugin {
	/// <summary>
	///     ASF will call this method for unrecognized commands.
	/// </summary>
	/// <param name="bot">Bot object related to this callback.</param>
	/// <param name="access">Access of user executing the command.</param>
	/// <param name="message">Command message in its raw format, stripped of <see cref="GlobalConfig.CommandPrefix" />.</param>
	/// <param name="args">Pre-parsed message using standard ASF delimiters.</param>
	/// <param name="steamID">Optionally, steamID of the user who executed the command - may not be available with value of 0 (e.g. ASF API).</param>
	/// <returns>Response to the command, or null/empty (as the task value) if the command isn't handled by this plugin.</returns>
	Task<string?> OnBotCommand(Bot bot, EAccess access, string message, string[] args, ulong steamID = 0);
}
