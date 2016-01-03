using System;
using System.ServiceModel;

namespace ArchiSteamFarm {
	[ServiceContract]
	internal interface IWCF {
		[OperationContract]
		string SendCommand(string command);
	}

	internal class WCF : IWCF {
		private const string URL = "http://localhost:1242/ASF"; // 1242 = 1024 + A(65) + S(83) + F(70)

		private ServiceHost ServiceHost;

		internal void StartServer() {
			if (ServiceHost != null) {
				return;
			}

			Logging.LogGenericNotice("WCF", "Starting WCF server...");
			ServiceHost = new ServiceHost(typeof(WCF));
			ServiceHost.AddServiceEndpoint(typeof(IWCF), new BasicHttpBinding(), URL);

			try {
				ServiceHost.Open();
			} catch (AddressAccessDeniedException) {
				Logging.LogGenericWarning("WCF", "WCF service could not be started because of AddressAccessDeniedException");
				Logging.LogGenericWarning("WCF", "If you want to use WCF service provided by ASF, consider starting ASF as administrator, or giving proper permissions");
				return;
			} catch (Exception e) {
				Logging.LogGenericException("WCF", e);
				return;
			}

			Logging.LogGenericNotice("WCF", "WCF server ready!");
		}

		internal void StopServer() {
			if (ServiceHost == null) {
				return;
			}

			ServiceHost.Close();
			ServiceHost = null;
		}

		public string SendCommand(string command) {
			throw new NotImplementedException();
		}
	}
}
