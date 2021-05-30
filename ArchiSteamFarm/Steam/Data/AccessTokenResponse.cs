
using System.Diagnostics.CodeAnalysis;
using Newtonsoft.Json;

namespace ArchiSteamFarm.Steam.Data {
	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class AccessTokenResponse : EResultResponse {
		[JsonProperty(PropertyName = "data", Required = Required.Always)]
		internal AccessTokenData AccessTokenData { get; private set; } = new();
	}

	[SuppressMessage("ReSharper", "ClassCannotBeInstantiated")]
	internal sealed class AccessTokenData {
		[JsonProperty(PropertyName = "webapi_token", Required = Required.Always)]
		internal string WebAPIToken { get; private set; } = "";
	}
}
