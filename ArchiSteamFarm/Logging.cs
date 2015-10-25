using System;
using System.Runtime.CompilerServices;

namespace ArchiSteamFarm {
	internal static class Logging {
		private static void Log(string message) {
			Console.WriteLine(DateTime.Now + " " + message);
		}

		internal static void LogGenericError(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[!!] ERROR: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericException(string botName, Exception exception, [CallerMemberName] string previousMethodName = "") {
			Log("[!] EXCEPTION: " + previousMethodName + "() <" + botName + "> " + exception.Message);
		}

		internal static void LogGenericWarning(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[!] WARNING: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericInfo(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[*] INFO: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericDebug(string botName, string message, [CallerMemberName] string previousMethodName = "") {
			Log("[#] DEBUG: " + previousMethodName + "() <" + botName + "> " + message);
		}

		internal static void LogGenericDebug(string message, [CallerMemberName] string previousMethodName = "") {
			LogGenericDebug("DEBUG", message, previousMethodName);
        }

		internal static void LogNullError(string nullObjectName, [CallerMemberName] string previousMethodName = "") {
			LogGenericError(nullObjectName + " is null!", previousMethodName);
		}
	}
}
