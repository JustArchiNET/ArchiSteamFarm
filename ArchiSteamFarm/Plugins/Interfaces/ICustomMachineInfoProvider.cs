//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// |
// Copyright 2015-2023 Łukasz "JustArchi" Domeradzki
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
using JetBrains.Annotations;
using SteamKit2;

namespace ArchiSteamFarm.Plugins.Interfaces;

[Obsolete($"Use {nameof(IBotCustomMachineInfoProvider)} instead, this interface will be removed in the future version")]
[PublicAPI]
public interface ICustomMachineInfoProvider : IPlugin {
	/// <summary>
	///     ASF will use this property as the <see cref="IMachineInfoProvider" /> for the bots.
	///     Unless you know what you're doing, you should not implement this property yourself and let ASF decide.
	/// </summary>
	/// <returns><see cref="IMachineInfoProvider" /> that will be used for the bots.</returns>
	IMachineInfoProvider MachineInfoProvider { get; }
}
