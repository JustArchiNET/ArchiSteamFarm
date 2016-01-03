using System;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace ArchiSteamFarm {
	[ServiceContract]
	internal interface IWCF {
		[OperationContract]
		string HandleCommand(string input);
	}

	internal class WCF : IWCF {

		private const string URL = "http://localhost:1242/ASF"; // 1242 = 1024 + A(65) + S(83) + F(70)

		private ServiceHost ServiceHost;
		private Client Client;

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

		internal string SendCommand(string input) {
			if (Client == null) {
				Client = new Client(new BasicHttpBinding(), new EndpointAddress(URL));
			}

			return Client.HandleCommand(input);
		}

		public string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				return null;
			}

			string[] args = input.Split(' ');
			if (args.Length < 2) {
				return "Too few arguments, expected: <Command> <BotName> <ExtraArgs>";
			}

			string command = args[0];
			string botName = args[1];
			string argument;
			if (args.Length > 2) {
				argument = args[3];
			}

			switch (command) {
				case "status":
					return Bot.ResponseStatus(botName);
				default:
					return "Unrecognized command: " + command;
			}
		}
	}

	internal class Client : ClientBase<IWCF>, IWCF {
		internal Client(Binding binding, EndpointAddress address) : base(binding, address) { }

		public string HandleCommand(string input) {
			return Channel.HandleCommand(input);
		}
	}
}
