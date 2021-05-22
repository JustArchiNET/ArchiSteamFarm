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

using System;
using System.Net;
using System.Net.Http;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Web.Responses {
	public class BasicResponse {
		[PublicAPI]
		public HttpStatusCode StatusCode { get; }

		internal readonly Uri FinalUri;

		internal BasicResponse(HttpResponseMessage httpResponseMessage) {
			if (httpResponseMessage == null) {
				throw new ArgumentNullException(nameof(httpResponseMessage));
			}

			FinalUri = httpResponseMessage.Headers.Location ?? httpResponseMessage.RequestMessage?.RequestUri ?? throw new InvalidOperationException();
			StatusCode = httpResponseMessage.StatusCode;
		}

		internal BasicResponse(BasicResponse basicResponse) {
			if (basicResponse == null) {
				throw new ArgumentNullException(nameof(basicResponse));
			}

			FinalUri = basicResponse.FinalUri;
			StatusCode = basicResponse.StatusCode;
		}
	}
}
