using Newtonsoft.Json;

namespace SteamAuth {
	// TODO: Converter code will be removed soon
	public class SteamGuardAccount {
		[JsonProperty("shared_secret")]
		public string SharedSecret { get; set; }

		[JsonProperty("serial_number")]
		public string SerialNumber { get; set; }

		[JsonProperty("revocation_code")]
		public string RevocationCode { get; set; }

		[JsonProperty("uri")]
		public string URI { get; set; }

		[JsonProperty("server_time")]
		public long ServerTime { get; set; }

		[JsonProperty("account_name")]
		public string AccountName { get; set; }

		[JsonProperty("token_gid")]
		public string TokenGID { get; set; }

		[JsonProperty("identity_secret")]
		public string IdentitySecret { get; set; }

		[JsonProperty("secret_1")]
		public string Secret1 { get; set; }

		[JsonProperty("status")]
		public int Status { get; set; }

		[JsonProperty("device_id")]
		public string DeviceID { get; set; }

		[JsonProperty("fully_enrolled")]
		public bool FullyEnrolled { get; set; }

	}
}
