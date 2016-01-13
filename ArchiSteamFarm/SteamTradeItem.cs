
namespace ArchiSteamFarm {
	public class SteamTradeItem {
		public int appid { get; set; }
		public int contextid { get; set; }
		public int amount { get; set; }
		public string assetid { get; set; }
		public SteamTradeItem (int aid, int cid, int am, string asset) {
			appid = aid;
			contextid = cid;
			amount = am;
			assetid = asset;
		}
	}
}
