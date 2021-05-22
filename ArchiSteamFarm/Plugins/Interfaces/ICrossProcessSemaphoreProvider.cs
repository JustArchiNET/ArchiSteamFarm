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

using System.Threading.Tasks;
using ArchiSteamFarm.Helpers;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Plugins.Interfaces {
	[PublicAPI]
	public interface ICrossProcessSemaphoreProvider : IPlugin {
		/// <summary>
		///     ASF will call this method when initializing instance of <see cref="ICrossProcessSemaphore" /> for its internal limiters.
		/// </summary>
		/// <param name="resourceName">Unique resource name provided by ASF for identification purposes.</param>
		/// <returns>Concrete implementation of <see cref="ICrossProcessSemaphore" /> providing required functionality. It's allowed to return null if you want to use ASF's default implementation for specified resource instead.</returns>
		Task<ICrossProcessSemaphore?> GetCrossProcessSemaphore(string resourceName);
	}
}
