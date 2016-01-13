
namespace ArchiSteamFarm {
	public class SteamTradeOfferRequest {
		public bool newversion { get; set; }
		public int version { get; set; }
		public SteamTradeItemList me { get; set; }
		public SteamTradeItemList them { get; set; }
		public SteamTradeOfferRequest (bool nv, int v, SteamTradeItemList m, SteamTradeItemList t) {
			newversion = nv;
			version = v;
			me = m;
			them = t;
		}
	}           
}
