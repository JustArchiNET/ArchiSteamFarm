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

using System;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces;

/// <inheritdoc />
/// <summary>
///     Implementing this interface allows you to be aware of ASF updates and execute appropriate logic that you need to happen before/after such update happens.
/// </summary>
[PublicAPI]
public interface IUpdateAware : IPlugin {
	/// <summary>
	///     ASF will call this method after update to a particular ASF version has been finished, just before restart of the process.
	/// </summary>
	/// <param name="currentVersion">The current (old) version of ASF program.</param>
	/// <param name="newVersion">The target (new) version of ASF program.</param>
	Task OnUpdateFinished(Version currentVersion, Version newVersion);

	/// <summary>
	///     ASF will call this method before proceeding with an update to a particular ASF version.
	/// </summary>
	/// <param name="currentVersion">The current (old) version of ASF program.</param>
	/// <param name="newVersion">The target (new) version of ASF program.</param>
	Task OnUpdateProceeding(Version currentVersion, Version newVersion);
}
