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
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.Core;
using ArchiSteamFarm.Helpers.Json;
using ArchiSteamFarm.Localization;
using ArchiSteamFarm.NLog;
using ArchiSteamFarm.Storage;
using ArchiSteamFarm.Web.Responses;
using JetBrains.Annotations;

namespace ArchiSteamFarm.Web;

public sealed class WebBrowser : IDisposable {
	[PublicAPI]
	public const byte MaxTries = 5; // Defines maximum number of recommended tries for a single request

	internal const byte MaxConnections = 5; // Defines maximum number of connections per ServicePoint. Be careful, as it also defines maximum number of sockets in CLOSE_WAIT state

	private const ushort ExtendedTimeout = 600; // Defines timeout for WebBrowsers dealing with huge data (ASF update)
	private const byte MaxIdleTime = 15; // Defines in seconds, how long socket is allowed to stay in CLOSE_WAIT state after there are no connections to it

	[PublicAPI]
	public CookieContainer CookieContainer { get; } = new();

	[PublicAPI]
	public TimeSpan Timeout => HttpClient.Timeout;

	private readonly ArchiLogger ArchiLogger;
	private readonly HttpClient HttpClient;
	private readonly HttpClientHandler HttpClientHandler;

	internal WebBrowser(ArchiLogger archiLogger, IWebProxy? webProxy = null, bool extendedTimeout = false) {
		ArgumentNullException.ThrowIfNull(archiLogger);

		ArchiLogger = archiLogger;

		HttpClientHandler = new HttpClientHandler {
			AllowAutoRedirect = false, // This must be false if we want to handle custom redirection schemes such as "steammobile://"
			AutomaticDecompression = DecompressionMethods.All,
			CookieContainer = CookieContainer,
			MaxConnectionsPerServer = MaxConnections
		};

		if (webProxy != null) {
			HttpClientHandler.Proxy = webProxy;
			HttpClientHandler.UseProxy = true;

			if (webProxy.Credentials != null) {
				// We can be pretty sure that user knows what he's doing and that proxy indeed requires authentication, save roundtrip
				HttpClientHandler.PreAuthenticate = true;
			}
		}

		HttpClient = GenerateDisposableHttpClient(extendedTimeout);
	}

	public void Dispose() {
		HttpClient.Dispose();
		HttpClientHandler.Dispose();
	}

	[PublicAPI]
	public HttpClient GenerateDisposableHttpClient(bool extendedTimeout = false) {
		byte connectionTimeout = ASF.GlobalConfig?.ConnectionTimeout ?? GlobalConfig.DefaultConnectionTimeout;

		HttpClient result = new(HttpClientHandler, false) {
			DefaultRequestVersion = HttpVersion.Version30,
			Timeout = TimeSpan.FromSeconds(extendedTimeout ? ExtendedTimeout : connectionTimeout)
		};

		// Most web services expect that UserAgent is set, so we declare it globally
		// If you by any chance came here with a very "clever" idea of hiding your ass by changing default ASF user-agent then here is a very good advice from me: don't, for your own safety - you've been warned
		result.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(SharedInfo.PublicIdentifier, SharedInfo.Version.ToString()));
		result.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue($"({SharedInfo.BuildInfo.Variant}; {OS.Version.Replace("(", "", StringComparison.Ordinal).Replace(")", "", StringComparison.Ordinal)}; +{SharedInfo.ProjectURL})"));

		// Inform websites that we visit about our preference in language, if possible
		result.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en-US", 0.9));
		result.DefaultRequestHeaders.AcceptLanguage.Add(new StringWithQualityHeaderValue("en", 0.8));

		return result;
	}

	[PublicAPI]
	public async Task<BinaryResponse?> UrlGetToBinary(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, IProgress<byte>? progressReporter = null, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			StreamResponse? response = await UrlGetToStream(request, headers, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1, rateLimitingDelay, cancellationToken).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsRedirectionCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
						break;
					}
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						break;
					}
				}

				if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						continue;
					}
				}

				if (response.Content == null) {
					throw new InvalidOperationException(nameof(response.Content));
				}

				if (response.Length > Array.MaxLength) {
					throw new InvalidOperationException(nameof(response.Length));
				}

				progressReporter?.Report(0);

				MemoryStream ms = new((int) response.Length);

				await using (ms.ConfigureAwait(false)) {
					byte batch = 0;
					long readThisBatch = 0;
					long batchIncreaseSize = response.Length / 100;

					ArrayPool<byte> bytePool = ArrayPool<byte>.Shared;

					// This is HttpClient's buffer, using more doesn't make sense
					byte[] buffer = bytePool.Rent(8192);

					try {
						while (response.Content.CanRead) {
							int read = await response.Content.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);

							if (read <= 0) {
								break;
							}

							// Report progress in-between downloading only if file is big enough to justify it
							// Current logic below will report progress if file is bigger than ~800 KB
							if (batchIncreaseSize >= buffer.Length) {
								readThisBatch += read;

								for (; (readThisBatch >= batchIncreaseSize) && (batch < 99); readThisBatch -= batchIncreaseSize) {
									// We need a copy of variable being passed when in for loops, as loop will proceed before our event is launched
									byte progress = ++batch;

									progressReporter?.Report(progress);
								}
							}

							await ms.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
						}
					} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
						throw;
					} catch (Exception e) {
						ArchiLogger.LogGenericWarningException(e);
						ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

						return null;
					} finally {
						bytePool.Return(buffer);
					}

					progressReporter?.Report(100);

					return new BinaryResponse(response, ms.ToArray());
				}
			}
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<HtmlDocumentResponse?> UrlGetToHtmlDocument(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			StreamResponse? response = await UrlGetToStream(request, headers, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1, rateLimitingDelay, cancellationToken).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsRedirectionCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
						break;
					}
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						break;
					}
				}

				if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						continue;
					}
				}

				if (response.Content == null) {
					throw new InvalidOperationException(nameof(response.Content));
				}

				try {
					return await HtmlDocumentResponse.Create(response, cancellationToken).ConfigureAwait(false);
				} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
					throw;
				} catch (Exception e) {
					if ((requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnSuccess) && response.StatusCode.IsSuccessCode()) || (requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnErrors) && !response.StatusCode.IsSuccessCode())) {
						return new HtmlDocumentResponse(response);
					}

					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
				}
			}
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<ObjectResponse<T>?> UrlGetToJsonObject<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			StreamResponse? response = await UrlGetToStream(request, headers, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1, rateLimitingDelay, cancellationToken).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsRedirectionCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
						break;
					}
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						break;
					}
				}

				if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						continue;
					}
				}

				if (response.Content == null) {
					throw new InvalidOperationException(nameof(response.Content));
				}

				T? obj;

				try {
					obj = await response.Content.ToJsonObject<T>(cancellationToken).ConfigureAwait(false);
				} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
					throw;
				} catch (Exception e) {
					if ((requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnSuccess) && response.StatusCode.IsSuccessCode()) || (requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnErrors) && !response.StatusCode.IsSuccessCode())) {
						return new ObjectResponse<T>(response);
					}

					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					continue;
				}

				if (obj is null) {
					if ((requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnSuccess) && response.StatusCode.IsSuccessCode()) || (requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnErrors) && !response.StatusCode.IsSuccessCode())) {
						return new ObjectResponse<T>(response);
					}

					ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(obj)));

					continue;
				}

				return new ObjectResponse<T>(response, obj);
			}
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<StreamResponse?> UrlGetToStream(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			HttpResponseMessage? response = await InternalGet(request, headers, referer, requestOptions, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			if (response.StatusCode.IsRedirectionCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
					break;
				}
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					break;
				}
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					continue;
				}
			}

			return new StreamResponse(response, await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<BasicResponse?> UrlHead(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			using HttpResponseMessage? response = await InternalHead(request, headers, referer, requestOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (response == null) {
				continue;
			}

			if (response.StatusCode.IsRedirectionCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
					break;
				}
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					break;
				}
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					continue;
				}
			}

			return new BasicResponse(response);
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<BasicResponse?> UrlPost<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) where T : class {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			using HttpResponseMessage? response = await InternalPost(request, headers, data, referer, requestOptions, cancellationToken: cancellationToken).ConfigureAwait(false);

			if (response == null) {
				continue;
			}

			if (response.StatusCode.IsRedirectionCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
					break;
				}
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					break;
				}
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					continue;
				}
			}

			return new BasicResponse(response);
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<HtmlDocumentResponse?> UrlPostToHtmlDocument<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) where T : class {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			StreamResponse? response = await UrlPostToStream(request, headers, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1, rateLimitingDelay, cancellationToken).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsRedirectionCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
						break;
					}
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						break;
					}
				}

				if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						continue;
					}
				}

				if (response.Content == null) {
					throw new InvalidOperationException(nameof(response.Content));
				}

				try {
					return await HtmlDocumentResponse.Create(response, cancellationToken).ConfigureAwait(false);
				} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
					throw;
				} catch (Exception e) {
					if ((requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnSuccess) && response.StatusCode.IsSuccessCode()) || (requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnErrors) && !response.StatusCode.IsSuccessCode())) {
						return new HtmlDocumentResponse(response);
					}

					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));
				}
			}
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<ObjectResponse<TResult>?> UrlPostToJsonObject<TResult, TData>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, TData? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) where TData : class {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			StreamResponse? response = await UrlPostToStream(request, headers, data, referer, requestOptions | ERequestOptions.ReturnClientErrors, 1, rateLimitingDelay, cancellationToken).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			await using (response.ConfigureAwait(false)) {
				if (response.StatusCode.IsRedirectionCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
						break;
					}
				}

				if (response.StatusCode.IsClientErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
						break;
					}
				}

				if (response.StatusCode.IsServerErrorCode()) {
					if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
						continue;
					}
				}

				if (response.Content == null) {
					throw new InvalidOperationException(nameof(response.Content));
				}

				TResult? obj;

				try {
					obj = await response.Content.ToJsonObject<TResult>(cancellationToken).ConfigureAwait(false);
				} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
					throw;
				} catch (Exception e) {
					if ((requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnSuccess) && response.StatusCode.IsSuccessCode()) || (requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnErrors) && !response.StatusCode.IsSuccessCode())) {
						return new ObjectResponse<TResult>(response);
					}

					ArchiLogger.LogGenericWarningException(e);
					ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

					continue;
				}

				if (obj is null) {
					if ((requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnSuccess) && response.StatusCode.IsSuccessCode()) || (requestOptions.HasFlag(ERequestOptions.AllowInvalidBodyOnErrors) && !response.StatusCode.IsSuccessCode())) {
						return new ObjectResponse<TResult>(response);
					}

					ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorIsEmpty, nameof(obj)));

					continue;
				}

				return new ObjectResponse<TResult>(response, obj);
			}
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	[PublicAPI]
	public async Task<StreamResponse?> UrlPostToStream<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, byte maxTries = MaxTries, int rateLimitingDelay = 0, CancellationToken cancellationToken = default) where T : class {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentOutOfRangeException.ThrowIfZero(maxTries);
		ArgumentOutOfRangeException.ThrowIfNegative(rateLimitingDelay);

		for (byte i = 0; i < maxTries; i++) {
			if ((i > 0) && (rateLimitingDelay > 0)) {
				await Task.Delay(rateLimitingDelay, cancellationToken).ConfigureAwait(false);
			}

			HttpResponseMessage? response = await InternalPost(request, headers, data, referer, requestOptions, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

			if (response == null) {
				// Request timed out, try again
				continue;
			}

			if (response.StatusCode.IsRedirectionCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
					break;
				}
			}

			if (response.StatusCode.IsClientErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnClientErrors)) {
					break;
				}
			}

			if (response.StatusCode.IsServerErrorCode()) {
				if (!requestOptions.HasFlag(ERequestOptions.ReturnServerErrors)) {
					continue;
				}
			}

			return new StreamResponse(response, await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
		}

		ArchiLogger.LogGenericWarning(string.Format(CultureInfo.CurrentCulture, Strings.ErrorRequestFailedTooManyTimes, maxTries));
		ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.ErrorFailingRequest, request));

		return null;
	}

	internal static void Init() {
		// Set max connection limit from default of 2 to desired value
		ServicePointManager.DefaultConnectionLimit = MaxConnections;

		// Set max idle time from default of 100 seconds (100 * 1000) to desired value
		ServicePointManager.MaxServicePointIdleTime = MaxIdleTime * 1000;

		// Don't use Expect100Continue, we're sure about our POSTs, save some TCP packets
		ServicePointManager.Expect100Continue = false;

		// Reuse ports if possible
		ServicePointManager.ReusePort = true;
	}

	private async Task<HttpResponseMessage?> InternalGet(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(httpCompletionOption)) {
			throw new InvalidEnumArgumentException(nameof(httpCompletionOption), (int) httpCompletionOption, typeof(HttpCompletionOption));
		}

		return await InternalRequest<object>(request, HttpMethod.Get, headers, null, referer, requestOptions, httpCompletionOption, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	private async Task<HttpResponseMessage?> InternalHead(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default) {
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(httpCompletionOption)) {
			throw new InvalidEnumArgumentException(nameof(httpCompletionOption), (int) httpCompletionOption, typeof(HttpCompletionOption));
		}

		return await InternalRequest<object>(request, HttpMethod.Head, headers, null, referer, requestOptions, httpCompletionOption, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	private async Task<HttpResponseMessage?> InternalPost<T>(Uri request, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, CancellationToken cancellationToken = default) where T : class {
		ArgumentNullException.ThrowIfNull(request);

		if (!Enum.IsDefined(httpCompletionOption)) {
			throw new InvalidEnumArgumentException(nameof(httpCompletionOption), (int) httpCompletionOption, typeof(HttpCompletionOption));
		}

		return await InternalRequest(request, HttpMethod.Post, headers, data, referer, requestOptions, httpCompletionOption, cancellationToken: cancellationToken).ConfigureAwait(false);
	}

	[UnconditionalSuppressMessage("AssemblyLoadTrimming", "IL2026:RequiresUnreferencedCode", Justification = "We don't care about trimmed assemblies, as we need it to work only with the known (used) ones")]
	private async Task<HttpResponseMessage?> InternalRequest<T>(Uri request, HttpMethod httpMethod, IReadOnlyCollection<KeyValuePair<string, string>>? headers = null, T? data = null, Uri? referer = null, ERequestOptions requestOptions = ERequestOptions.None, HttpCompletionOption httpCompletionOption = HttpCompletionOption.ResponseContentRead, byte maxRedirections = MaxTries, CancellationToken cancellationToken = default) where T : class {
		ArgumentNullException.ThrowIfNull(request);
		ArgumentNullException.ThrowIfNull(httpMethod);

		if (!Enum.IsDefined(httpCompletionOption)) {
			throw new InvalidEnumArgumentException(nameof(httpCompletionOption), (int) httpCompletionOption, typeof(HttpCompletionOption));
		}

		HttpResponseMessage response;

		while (true) {
			using (HttpRequestMessage requestMessage = new(httpMethod, request)) {
				requestMessage.Version = HttpClient.DefaultRequestVersion;

				if (headers != null) {
					foreach ((string header, string value) in headers) {
						requestMessage.Headers.Add(header, value);
					}
				}

				if (data != null) {
					switch (data) {
						case HttpContent content:
							requestMessage.Content = content;

							break;
						case IReadOnlyCollection<KeyValuePair<string, string>> nameValueCollection:
							try {
								requestMessage.Content = new FormUrlEncodedContent(nameValueCollection);
							} catch (UriFormatException) {
								requestMessage.Content = new StringContent(string.Join('&', nameValueCollection.Select(static kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}")), null, "application/x-www-form-urlencoded");
							}

							break;
						case string text:
							requestMessage.Content = new StringContent(text);

							break;
						default:
							requestMessage.Content = JsonContent.Create(data, options: JsonUtilities.DefaultJsonSerialierOptions);

							break;
					}

					// Compress the request if caller specified it, so they know that the server supports it, and the content is not compressed yet
					if (requestOptions.HasFlag(ERequestOptions.CompressRequest) && (requestMessage.Content.Headers.ContentEncoding.Count == 0)) {
						HttpContent originalContent = requestMessage.Content;

						requestMessage.Content = await WebBrowserUtilities.CreateCompressedHttpContent(originalContent).ConfigureAwait(false);

						if (data is not HttpContent) {
							// We don't need to keep old HttpContent around anymore, help GC
							originalContent.Dispose();
						}
					}
				}

				if (referer != null) {
					requestMessage.Headers.Referrer = referer;
				}

				if (Debugging.IsUserDebugging) {
					ArchiLogger.LogGenericDebug($"{httpMethod} {request}");
				}

				try {
					response = await HttpClient.SendAsync(requestMessage, httpCompletionOption, cancellationToken).ConfigureAwait(false);
				} catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
					throw;
				} catch (Exception e) {
					ArchiLogger.LogGenericDebuggingException(e);

					return null;
				} finally {
					if (data is HttpContent) {
						// We reset the request content to null, as our http content will get disposed otherwise, and we still need it for subsequent calls, such as redirections or retries
						requestMessage.Content = null;
					}
				}
			}

			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug($"{response.StatusCode} <- {httpMethod} {request}");
			}

			if (response.IsSuccessStatusCode) {
				return response;
			}

			// WARNING: We still have not disposed response by now, make sure to dispose it ASAP if we're not returning it!
			if (response.StatusCode.IsRedirectionCode() && (maxRedirections > 0)) {
				if (requestOptions.HasFlag(ERequestOptions.ReturnRedirections)) {
					// User wants to handle it manually, that's alright
					return response;
				}

				Uri? redirectUri = response.Headers.Location;

				if (redirectUri == null) {
					ArchiLogger.LogNullError(redirectUri);

					return null;
				}

				if (redirectUri.IsAbsoluteUri) {
					switch (redirectUri.Scheme) {
						case "http" or "https":
							break;
						case "steammobile":
							// Those redirections are invalid, but we're aware of that and we have extra logic for them
							return response;
						default:
							// We have no clue about those, but maybe HttpClient can handle them for us
							ArchiLogger.LogGenericError(string.Format(CultureInfo.CurrentCulture, Strings.WarningUnknownValuePleaseReport, nameof(redirectUri.Scheme), redirectUri.Scheme));

							break;
					}
				} else {
					redirectUri = new Uri(request, redirectUri);
				}

				switch (response.StatusCode) {
					case HttpStatusCode.MovedPermanently: // Per https://tools.ietf.org/html/rfc7231#section-6.4.2, a 301 redirect may be performed using a GET request
					case HttpStatusCode.Redirect: // Per https://tools.ietf.org/html/rfc7231#section-6.4.3, a 302 redirect may be performed using a GET request
					case HttpStatusCode.SeeOther: // Per https://tools.ietf.org/html/rfc7231#section-6.4.4, a 303 redirect should be performed using a GET request
						if (httpMethod != HttpMethod.Head) {
							httpMethod = HttpMethod.Get;
						}

						// Data doesn't make any sense for a fetch request, clear it in case it's being used
						data = null;

						break;
				}

				response.Dispose();

				// Per https://tools.ietf.org/html/rfc7231#section-7.1.2, a redirect location without a fragment should inherit the fragment from the original URI
				if (!string.IsNullOrEmpty(request.Fragment) && string.IsNullOrEmpty(redirectUri.Fragment)) {
					redirectUri = new UriBuilder(redirectUri) { Fragment = request.Fragment }.Uri;
				}

				request = redirectUri;
				maxRedirections--;

				continue;
			}

			break;
		}

		if (!Debugging.IsUserDebugging) {
			ArchiLogger.LogGenericDebug($"{response.StatusCode} <- {httpMethod} {request}");
		}

		if (response.StatusCode.IsClientErrorCode()) {
			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Content, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
			}

			// Do not retry on client errors
			return response;
		}

		if (requestOptions.HasFlag(ERequestOptions.ReturnServerErrors) && response.StatusCode.IsServerErrorCode()) {
			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Content, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
			}

			// Do not retry on server errors in this case
			return response;
		}

		using (response) {
			if (Debugging.IsUserDebugging) {
				ArchiLogger.LogGenericDebug(string.Format(CultureInfo.CurrentCulture, Strings.Content, await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false)));
			}

			return null;
		}
	}

	[Flags]
	public enum ERequestOptions : byte {
		None = 0,
		ReturnClientErrors = 1,
		ReturnServerErrors = 2,
		ReturnRedirections = 4,
		AllowInvalidBodyOnSuccess = 8,
		AllowInvalidBodyOnErrors = 16,
		CompressRequest = 32
	}
}
