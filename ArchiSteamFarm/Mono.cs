using System;
using System.Reflection;

namespace ArchiSteamFarm {
	internal static class Mono {
		internal static bool RequiresWorkaroundForBug41701() {
			// https://bugzilla.xamarin.com/show_bug.cgi?id=41701
			Version version = GetMonoVersion();
			if (version == null) {
				return false;
			}

			return version >= new Version(4, 4);
		}

		private static Version GetMonoVersion() {
			Type type = Type.GetType("Mono.Runtime");
			if (type == null) {
				return null; // OK, not Mono
			}

			MethodInfo displayName = type.GetMethod("GetDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
			if (displayName == null) {
				Logging.LogNullError(nameof(displayName));
				return null;
			}

			string versionString = (string) displayName.Invoke(null, null);
			if (string.IsNullOrEmpty(versionString)) {
				Logging.LogNullError(nameof(versionString));
				return null;
			}

			int index = versionString.IndexOf(' ');
			if (index <= 0) {
				Logging.LogNullError(nameof(index));
				return null;
			}

			versionString = versionString.Substring(0, index);

			Version version;
			if (Version.TryParse(versionString, out version)) {
				return version;
			}

			Logging.LogNullError(nameof(version));
			return null;
		}
	}
}
