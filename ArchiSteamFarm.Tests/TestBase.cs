// ----------------------------------------------------------------------------------------------
//     _                _      _  ____   _                           _____
//    / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
//   / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
//  / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
// /_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|
// ----------------------------------------------------------------------------------------------
// |
// Copyright 2015-2026 Łukasz "JustArchi" Domeradzki
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
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Web;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace ArchiSteamFarm.Tests;

internal abstract class TestBase {
	protected CancellationToken CancellationToken => TestContext.CancellationToken;

	private readonly TestContext TestContext;

	internal TestBase(TestContext testContext) {
		ArgumentNullException.ThrowIfNull(testContext);

		TestContext = testContext;
	}

	protected void Print<T>(T obj) => TestContext.WriteLine(obj?.ToJsonText(true) ?? "null");

	protected async Task<T> WithWebBrowser<T>(Func<CancellationToken, Task<T>> action) {
		ArgumentNullException.ThrowIfNull(action);

		WebBrowser? previousWebBrowser = ASF.WebBrowser;

		PropertyInfo webBrowserProperty = typeof(ASF).GetProperty(nameof(ASF.WebBrowser)) ?? throw new InvalidOperationException(nameof(ASF.WebBrowser));

		using WebBrowser webBrowser = new(new ArchiLogger("Test"));

		webBrowserProperty.SetValue(null, webBrowser);

		try {
			return await action(CancellationToken).ConfigureAwait(false);
		} finally {
			webBrowserProperty.SetValue(null, previousWebBrowser);
		}
	}
}
