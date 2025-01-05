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
using System.Runtime.CompilerServices;
#if ASF_SIGNED_BUILD
using ArchiSteamFarm;
#endif

[assembly: CLSCompliant(false)]

#if ASF_SIGNED_BUILD
[assembly: InternalsVisibleTo($"ArchiSteamFarm.Tests, PublicKey={SharedInfo.PublicKey}")]
[assembly: InternalsVisibleTo($"ArchiSteamFarm.CustomPlugins.SignInWithSteam, PublicKey={SharedInfo.PublicKey}")]
[assembly: InternalsVisibleTo($"ArchiSteamFarm.OfficialPlugins.ItemsMatcher, PublicKey={SharedInfo.PublicKey}")]
[assembly: InternalsVisibleTo($"ArchiSteamFarm.OfficialPlugins.MobileAuthenticator, PublicKey={SharedInfo.PublicKey}")]
[assembly: InternalsVisibleTo($"ArchiSteamFarm.OfficialPlugins.Monitoring, PublicKey={SharedInfo.PublicKey}")]
[assembly: InternalsVisibleTo($"ArchiSteamFarm.OfficialPlugins.SteamTokenDumper, PublicKey={SharedInfo.PublicKey}")]
#else
[assembly: InternalsVisibleTo("ArchiSteamFarm.Tests")]
[assembly: InternalsVisibleTo("ArchiSteamFarm.CustomPlugins.SignInWithSteam")]
[assembly: InternalsVisibleTo("ArchiSteamFarm.OfficialPlugins.ItemsMatcher")]
[assembly: InternalsVisibleTo("ArchiSteamFarm.OfficialPlugins.MobileAuthenticator")]
[assembly: InternalsVisibleTo("ArchiSteamFarm.OfficialPlugins.Monitoring")]
[assembly: InternalsVisibleTo("ArchiSteamFarm.OfficialPlugins.SteamTokenDumper")]
#endif
