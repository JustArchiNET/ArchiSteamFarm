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

using System.Text.Json.Serialization;

namespace ArchiSteamFarm.Plugins.Interfaces;

/// <inheritdoc />
/// <summary>
///     Implementing this interface allows you to provide your own (custom) web interface files that will be exposed by standard ASF's IPC interface. In order to achieve that, you must include a directory with your web interface (html, css, js) files together with your plugin's DLL assembly, then specify path to it in <see cref="PhysicalPath" /> and finally the path under which you want to host those files in <see cref="WebPath" />.
/// </summary>
public interface IWebInterface : IPlugin {
	/// <summary>
	///     Specifies physical path to static WWW files provided by the plugin. Can be either relative to plugin's assembly location, or absolute. Default value of "www" assumes that you ship "www" directory together with your plugin's main DLL assembly, similar to ASF.
	/// </summary>
	/// <example>www</example>
	/// <remarks>You'll need to ship this folder together with your plugin for the interface to work.</remarks>
	string PhysicalPath => "www";

	/// <summary>
	///     Specifies web path (address) under which ASF should host your static WWW files in <see cref="PhysicalPath" /> directory. Default value of "/" allows you to override default ASF files and gives you full flexibility in your <see cref="PhysicalPath" /> directory. However, you can instead host your files under some other fixed location specified here, such as "/MyPlugin", which is especially useful if you want to have your own default index.html in addition to the one provided by us (ASF-ui).
	/// </summary>
	/// <example>/MyPlugin</example>
	/// <remarks>If you're using path other than default, ensure it does NOT end with a slash.</remarks>
	[JsonInclude]
	string WebPath => "/";
}
