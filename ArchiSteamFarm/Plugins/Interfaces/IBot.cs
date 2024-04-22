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

namespace ArchiSteamFarm.Plugins.Interfaces;

/// <inheritdoc />
/// <summary>
///     Implementing this interface allows you to register core settings-independent structures on per-bot basis.
/// </summary>
/// <remarks>If your logic requires bot's settings, consider implementing <see cref="IBotModules" /> instead. Implementing this interface might still make sense, e.g. for disposal of the structures upon <see cref="OnBotDestroy" />.</remarks>
[PublicAPI]
public interface IBot : IPlugin {
	/// <summary>
	///     ASF will call this method after removing its own references from it, e.g. after config removal.
	///     You should ensure that you'll remove any of your own references to this bot instance in timely manner.
	///     Doing so will allow the garbage collector to dispose the bot afterwards, refraining from doing so will create a "memory leak" by keeping the reference alive.
	/// </summary>
	/// <param name="bot">Bot object related to this callback.</param>
	Task OnBotDestroy(Bot bot);

	/// <summary>
	///     ASF will call this method after creating the bot object, e.g. after config creation.
	///     Bot config is not yet available at this stage. This function will execute only once for every bot object.
	/// </summary>
	/// <param name="bot">Bot object related to this callback.</param>
	Task OnBotInit(Bot bot);
}
