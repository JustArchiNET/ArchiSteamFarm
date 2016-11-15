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
using System.ServiceModel.Description;

namespace ArchiSteamFarm {
	[ServiceContract]
	internal interface IWCF {
		[OperationContract]
		string GetStatus();

		[OperationContract]
		string HandleCommand(string input);
	}

	internal sealed class WCF : IWCF, IDisposable {
		private static string URL = "http://localhost:1242/ASF";

		private ServiceHost ServiceHost;
		private Client Client;

		internal bool IsServerRunning => ServiceHost != null;

		internal static void Init() {
			if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHostname)) {
				Program.GlobalConfig.WCFHostname = Program.GetUserInput(ASF.EUserInputType.WCFHostname);
				if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHostname)) {
					return;
				}
			}

			URL = "http://" + Program.GlobalConfig.WCFHostname + ":" + Program.GlobalConfig.WCFPort + "/ASF";
		}

		public string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				ASF.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			if (Program.GlobalConfig.SteamOwnerID == 0) {
				return "Refusing to handle request because SteamOwnerID is not set!";
			}

			Bot bot = Bot.Bots.Values.FirstOrDefault();
			if (bot == null) {
				return "ERROR: No bots are enabled!";
			}

			string command = "!" + input;
			string output = bot.Response(Program.GlobalConfig.SteamOwnerID, command).Result; // TODO: This should be asynchronous

			ASF.ArchiLogger.LogGenericInfo("Answered to command: " + input + " with: " + output);
			return output;
		}

		public string GetStatus() => Program.GlobalConfig.SteamOwnerID == 0 ? "{}" : Bot.GetAPIStatus();

		public void Dispose() {
			StopClient();
			StopServer();
		}

		internal void StartServer() {
			if (IsServerRunning) {
				return;
			}

			ASF.ArchiLogger.LogGenericInfo("Starting WCF server...");

			try {
				ServiceHost = new ServiceHost(typeof(WCF), new Uri(URL));

				ServiceHost.Description.Behaviors.Add(new ServiceMetadataBehavior {
					HttpGetEnabled = true
				});

				ServiceHost.AddServiceEndpoint(ServiceMetadataBehavior.MexContractName, MetadataExchangeBindings.CreateMexHttpBinding(), "mex");
				ServiceHost.AddServiceEndpoint(typeof(IWCF), new BasicHttpBinding(), string.Empty);

				ServiceHost.Open();
				ASF.ArchiLogger.LogGenericInfo("WCF server ready!");
			} catch (AddressAccessDeniedException) {
				ASF.ArchiLogger.LogGenericError("WCF service could not be started because of AddressAccessDeniedException!");
				ASF.ArchiLogger.LogGenericWarning("If you want to use WCF service provided by ASF, consider starting ASF as administrator, or giving proper permissions!");
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
			}
		}

		internal void StopServer() {
			if (!IsServerRunning) {
				return;
			}

			if (ServiceHost.State != CommunicationState.Closed) {
				try {
					ServiceHost.Close();
				} catch (Exception e) {
					ASF.ArchiLogger.LogGenericException(e);
				}
			}

			ServiceHost = null;
		}

		internal string SendCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				ASF.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			if (Client == null) {
				Client = new Client(new BasicHttpBinding(), new EndpointAddress(URL));
			}

			return Client.HandleCommand(input);
		}

		private void StopClient() {
			if (Client == null) {
				return;
			}

			if (Client.State != CommunicationState.Closed) {
				Client.Close();
			}

			Client = null;
		}
	}

	internal sealed class Client : ClientBase<IWCF> {
		internal Client(Binding binding, EndpointAddress address) : base(binding, address) { }

		internal string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				ASF.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			try {
				return Channel.HandleCommand(input);
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				return null;
			}
		}
	}
}
