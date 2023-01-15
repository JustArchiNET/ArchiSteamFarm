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
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;

namespace ArchiSteamFarm.Web;

internal sealed class CompressedContent : StreamContent {
	private CompressedContent(Stream content) : base(content) => ArgumentNullException.ThrowIfNull(content);

	internal static async Task<CompressedContent> FromHttpContent(HttpContent content) {
		ArgumentNullException.ThrowIfNull(content);

		// We're going to create compressed stream and copy original content to it
		MemoryStream compressionOutput = new();

		(Stream compressionInput, string contentEncoding) = GetBestSupportedCompressionMethod(compressionOutput);

		await using (compressionInput.ConfigureAwait(false)) {
			await content.CopyToAsync(compressionInput).ConfigureAwait(false);
		}

		// Reset the position back to 0, so HttpClient can read it again
		compressionOutput.Position = 0;

		CompressedContent result = new(compressionOutput);

		foreach ((string? key, IEnumerable<string>? value) in content.Headers) {
			result.Headers.Add(key, value);
		}

		// Inform the server that we're sending compressed data
		result.Headers.ContentEncoding.Add(contentEncoding);

		return result;
	}

	private static (Stream CompressionInput, string ContentEncoding) GetBestSupportedCompressionMethod(Stream output) {
		ArgumentNullException.ThrowIfNull(output);

#if NETFRAMEWORK
		return (new GZipStream(output, CompressionLevel.Fastest, true), "gzip");
#else
		return (new BrotliStream(output, CompressionLevel.Fastest, true), "br");
#endif
	}
}
