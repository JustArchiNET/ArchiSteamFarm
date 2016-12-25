using System.Diagnostics.CodeAnalysis;

namespace ConfigGenerator.JSON {
	internal static class Steam {
		internal static class Item {
			[SuppressMessage("ReSharper", "UnusedMember.Global")]
			internal enum EType : byte {
				Unknown,
				BoosterPack,
				Coupon,
				Emoticon,
				Gift,
				FoilTradingCard,
				ProfileBackground,
				TradingCard,
				SteamGems
			}
		}
	}
}
