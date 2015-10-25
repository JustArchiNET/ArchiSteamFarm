namespace ArchiSteamFarm {
	internal sealed class SteamItem {
		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService
		internal string appid { get; set; }
		internal string contextid { get; set; }
		internal string assetid { get; set; }
		internal string currencyid { get; set; }
		internal string classid { get; set; }
		internal string instanceid { get; set; }
		internal string amount { get; set; }
		internal bool missing { get; set; }
	}
}
