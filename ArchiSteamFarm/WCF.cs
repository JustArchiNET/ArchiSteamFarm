/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2016 Łukasz "JustArchi" Domeradzki
 Contact: JustArchi@JustArchi.net

 Licensed under the Apache License, Version 2.0 (the "License");
 you may not use this file except in compliance with the License.
 You may obtain a copy of the License at

 http://www.apache.org/licenses/LICENSE-2.0
					
 Unless required by applicable law or agreed to in writing, software
 distributed under the License is distributed on an "AS IS" BASIS,
 WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 See the License for the specific language governing permissions and
 limitations under the License.

*/

using System;
using System.Linq;
using System.ServiceModel;
using System.ServiceModel.Channels;

namespace ArchiSteamFarm {
	[ServiceContract]
	internal interface IWCF {
		[OperationContract]
		string HandleCommand(string input);
	}

	internal sealed class WCF : IWCF, IDisposable {

		private static string URL = "http://localhost:1242/ASF";

		private ServiceHost ServiceHost;
		private Client Client;

		internal static void Init() {
			if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHostname)) {
				Program.GlobalConfig.WCFHostname = Program.GetUserInput(Program.EUserInputType.WCFHostname);
				if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHostname)) {
					return;
				}
			}

			URL = "http://" + Program.GlobalConfig.WCFHostname + ":" + Program.GlobalConfig.WCFPort + "/ASF";
		}

		public string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				Logging.LogNullError(nameof(input));
				return null;
			}

			Bot bot = Bot.Bots.Values.FirstOrDefault();
			if (bot == null) {
				return "ERROR: No bots are enabled!";
			}

			if (Program.GlobalConfig.SteamOwnerID == 0) {
				return "Refusing to handle request because SteamOwnerID is not set!";
			}

			string command = "!" + input;
			string output = bot.Response(Program.GlobalConfig.SteamOwnerID, command).Result; // TODO: This should be asynchronous

			Logging.LogGenericInfo("Answered to command: " + input + " with: " + output);
			return output;
		}

		public void Dispose() {
			if (ServiceHost != null) {
				ServiceHost.Close();
			}

			if (Client != null) {
				Client.Close();
			}
		}

		internal bool IsServerRunning() => ServiceHost != null;

		internal void StartServer() {
			if (ServiceHost != null) {
				return;
			}

			Logging.LogGenericInfo("Starting WCF server...");
			ServiceHost = new ServiceHost(typeof(WCF));
			ServiceHost.AddServiceEndpoint(typeof(IWCF), new BasicHttpBinding(), URL);

			try {
				ServiceHost.Open();
			} catch (AddressAccessDeniedException) {
				Logging.LogGenericWarning("WCF service could not be started because of AddressAccessDeniedException");
				Logging.LogGenericWarning("If you want to use WCF service provided by ASF, consider starting ASF as administrator, or giving proper permissions");
				return;
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return;
			}

			Logging.LogGenericInfo("WCF server ready!");
		}

		internal void StopServer() {
			if (ServiceHost == null) {
				return;
			}

			ServiceHost.Close();
			ServiceHost = null;
		}

		internal string SendCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				Logging.LogNullError(nameof(input));
				return null;
			}

			if (Client == null) {
				Client = new Client(new BasicHttpBinding(), new EndpointAddress(URL));
			}

			return Client.HandleCommand(input);
		}
	}

	internal sealed class Client : ClientBase<IWCF>, IWCF {
		internal Client(Binding binding, EndpointAddress address) : base(binding, address) { }

		public string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				Logging.LogNullError(nameof(input));
				return null;
			}

			try {
				return Channel.HandleCommand(input);
			} catch (Exception e) {
				Logging.LogGenericException(e);
				return null;
			}
		}
	}
}
