using System.Collections.Generic;

namespace ArchiSteamFarm {
	public class SteamTradeItemList {
		public List<SteamTradeItem> assets { get; set; }
		public List<string> currency { get; set; }
		public bool ready { get; set; }
		public SteamTradeItemList() {
			assets = new List<SteamTradeItem>();
			currency = new List<string>();
			ready = false;
		}
	}
}
