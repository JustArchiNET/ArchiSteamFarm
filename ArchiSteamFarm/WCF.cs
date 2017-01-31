/*
    _                _      _  ____   _                           _____
   / \    _ __  ___ | |__  (_)/ ___| | |_  ___   __ _  _ __ ___  |  ___|__ _  _ __  _ __ ___
  / _ \  | '__|/ __|| '_ \ | |\___ \ | __|/ _ \ / _` || '_ ` _ \ | |_  / _` || '__|| '_ ` _ \
 / ___ \ | |  | (__ | | | || | ___) || |_|  __/| (_| || | | | | ||  _|| (_| || |   | | | | | |
/_/   \_\|_|   \___||_| |_||_||____/  \__|\___| \__,_||_| |_| |_||_|   \__,_||_|   |_| |_| |_|

 Copyright 2015-2017 Łukasz "JustArchi" Domeradzki
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
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	[ServiceContract]
	internal interface IWCF {
		[OperationContract]
		string GetStatus();

		[OperationContract]
		string HandleCommand(string input);
	}

	internal sealed class WCF : IWCF, IDisposable {
		private static string URL = "net.tcp://127.0.0.1:1242/ASF";

		internal bool IsServerRunning => ServiceHost != null;

		private Client Client;
		private ServiceHost ServiceHost;

		public void Dispose() {
			StopClient();
			StopServer();
		}

		public string GetStatus() => Program.GlobalConfig.SteamOwnerID == 0 ? "{}" : Bot.GetAPIStatus();

		public string HandleCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				ASF.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			if (Program.GlobalConfig.SteamOwnerID == 0) {
				return Strings.ErrorWCFAccessDenied;
			}

			Bot bot = Bot.Bots.Values.FirstOrDefault();
			if (bot == null) {
				return Strings.ErrorNoBotsDefined;
			}

			string command = "!" + input;

			// TODO: This should be asynchronous, but for some reason Mono doesn't return any WCF output if it is
			// We must keep it synchronous until either Mono gets fixed, or culprit for freeze located (and corrected)
			string output = bot.Response(Program.GlobalConfig.SteamOwnerID, command).Result;

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.WCFAnswered, input, output));
			return output;
		}

		internal static void Init() {
			if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHost)) {
				Program.GlobalConfig.WCFHost = Program.GetUserInput(ASF.EUserInputType.WCFHostname);
				if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHost)) {
					return;
				}
			}

			URL = "net.tcp://" + Program.GlobalConfig.WCFHost + ":" + Program.GlobalConfig.WCFPort + "/ASF";
		}

		internal string SendCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				ASF.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.WCFSendingCommand, input, URL));

			if (Client == null) {
				Client = new Client(
					new NetTcpBinding {
						// We use SecurityMode.None for Mono compatibility
						// Yes, also on Windows, for Mono<->Windows communication
						Security = { Mode = SecurityMode.None },
						SendTimeout = new TimeSpan(0, 0, Program.GlobalConfig.ConnectionTimeout)
					},
					new EndpointAddress(URL)
				);
			}

			return Client.HandleCommand(input);
		}

		internal void StartServer() {
			if (IsServerRunning) {
				return;
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.WCFStarting, URL));

			try {
				ServiceHost = new ServiceHost(typeof(WCF), new Uri(URL));
				ServiceHost.AddServiceEndpoint(
					typeof(IWCF),
					new NetTcpBinding {
						// We use SecurityMode.None for Mono compatibility
						// Yes, also on Windows, for Mono<->Windows communication
						Security = { Mode = SecurityMode.None },
						SendTimeout = new TimeSpan(0, 0, Program.GlobalConfig.ConnectionTimeout)
					},
					string.Empty
				);
				ServiceHost.Open();

				ASF.ArchiLogger.LogGenericInfo(Strings.WCFReady);
			} catch (AddressAccessDeniedException) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorWCFAddressAccessDeniedException);
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