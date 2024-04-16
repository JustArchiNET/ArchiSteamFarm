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
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using ArchiSteamFarm.IPC.Responses;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ArchiSteamFarm.IPC.Controllers.Api;

[ApiController]
[Produces("application/json")]
[Route("/HealthCheck")]
public sealed class HealthCheckController : ControllerBase {
	private readonly HealthCheckService HealthCheckService;

	public HealthCheckController(HealthCheckService healthCheckService) {
		ArgumentNullException.ThrowIfNull(healthCheckService);

		HealthCheckService = healthCheckService;
	}

	[HttpGet]
	[ProducesResponseType(typeof(HealthCheckResponse), (int) HttpStatusCode.OK)]
	[ProducesResponseType(typeof(HealthCheckResponse), (int) HttpStatusCode.ServiceUnavailable)]
	public async Task<IActionResult> Get() {
		CancellationToken cancellationToken = HttpContext.RequestAborted;

		HealthReport report = await HealthCheckService.CheckHealthAsync(cancellationToken).ConfigureAwait(false);

		HealthCheckResponse response = new(report);

		return response.Status == HealthStatus.Healthy ? Ok(response) : StatusCode((int) HttpStatusCode.ServiceUnavailable, response);
	}
}
