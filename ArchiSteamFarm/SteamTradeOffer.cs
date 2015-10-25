using SteamKit2;
using System.Collections.Generic;

namespace ArchiSteamFarm {
	internal sealed class SteamTradeOffer {
		// REF: https://developer.valvesoftware.com/wiki/Steam_Web_API/IEconService
		internal enum ETradeOfferState {
			Unknown,
			Invalid,
			Active,
			Accepted,
			Countered,
			Expired,
			Canceled,
			Declined,
			InvalidItems,
			EmailPending,
			EmailCanceled
		}

		internal string tradeofferid { get; set; }
		internal int accountid_other { get; set; }
		internal string message { get; set; }
		internal int expiration_time { get; set; }
		internal ETradeOfferState trade_offer_state { get; set; }
		internal List<SteamItem> items_to_give { get; set; }
		internal List<SteamItem> items_to_receive { get; set; }
		internal bool is_our_offer { get; set; }
		internal int time_created { get; set; }
		internal int time_updated { get; set; }
		internal bool from_real_time_trade { get; set; }

		// Extra
		internal ulong OtherSteamID64 {
			get {
				return new SteamID((uint) accountid_other, EUniverse.Public, EAccountType.Individual).ConvertToUInt64();
			}
			private set { }
		}
	}
}
