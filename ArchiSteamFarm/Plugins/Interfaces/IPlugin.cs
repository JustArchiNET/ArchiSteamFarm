// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2025 Łukasz "JustArchi" Domeradzki
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
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces;

/// <summary>
///     Implementing this interface allows you to register your plugin in ASF, in turn providing you a way to implement your own custom logic.
/// </summary>
[PublicAPI]
public interface IPlugin {
	/// <summary>
	///     ASF will use this property as general plugin identifier for the user.
	/// </summary>
	/// <returns>String that will be used as the name of this plugin.</returns>
	[JsonInclude]
	string Name { get; }

	/// <summary>
	///     ASF will use this property as version indicator of your plugin to the user.
	///     You have a freedom in deciding what versioning you want to use, this is for identification purposes only.
	/// </summary>
	/// <returns>Version that will be shown to the user when plugin is loaded.</returns>
	[JsonInclude]
	Version Version { get; }

	/// <summary>
	///     ASF will call this method right after plugin initialization.
	/// </summary>
	Task OnLoaded();
}
