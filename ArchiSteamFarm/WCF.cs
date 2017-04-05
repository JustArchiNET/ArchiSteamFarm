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
using System.ServiceModel.Description;
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
		internal bool IsServerRunning => ServiceHost != null;

		private Client Client;
		private ServiceHost ServiceHost;

		public void Dispose() {
			StopClient();
			StopServer();
		}

		public string GetStatus() => Program.GlobalConfig.SteamOwnerID == 0 ? "{}" : Bot.GetAPIStatus(Bot.Bots);

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

			if (input[0] != '!') {
				input = "!" + input;
			}

			// TODO: This should be asynchronous, but for some reason Mono doesn't return any WCF output if it is
			// We must keep it synchronous until either Mono gets fixed, or culprit for freeze located (and corrected)
			string output = bot.Response(Program.GlobalConfig.SteamOwnerID, input).Result;

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.WCFAnswered, input, output));
			return output;
		}

		internal static void Init() {
			if (string.IsNullOrEmpty(Program.GlobalConfig.WCFHost)) {
				Program.GlobalConfig.WCFHost = Program.GetUserInput(ASF.EUserInputType.WCFHostname);
			}
		}

		internal string SendCommand(string input) {
			if (string.IsNullOrEmpty(input)) {
				ASF.ArchiLogger.LogNullError(nameof(input));
				return null;
			}

			Binding binding = GetTargetBinding();
			if (binding == null) {
				ASF.ArchiLogger.LogNullError(nameof(binding));
				return null;
			}

			string url = GetUrlFromBinding(binding);
			if (string.IsNullOrEmpty(url)) {
				ASF.ArchiLogger.LogNullError(nameof(url));
				return null;
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.WCFSendingCommand, input, url));

			if (Client == null) {
				Client = new Client(
					binding,
					new EndpointAddress(url)
				);
			}

			return Client.HandleCommand(input);
		}

		internal void StartServer() {
			if (IsServerRunning) {
				return;
			}

			Binding binding = GetTargetBinding();
			if (binding == null) {
				ASF.ArchiLogger.LogNullError(nameof(binding));
				return;
			}

			string url = GetUrlFromBinding(binding);
			if (string.IsNullOrEmpty(url)) {
				ASF.ArchiLogger.LogNullError(nameof(url));
				return;
			}

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.WCFStarting, url));

			Uri uri;

			try {
				uri = new Uri(url);
			} catch (UriFormatException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			ServiceHost = new ServiceHost(typeof(WCF), uri);

			ServiceHost.AddServiceEndpoint(
				typeof(IWCF),
				binding,
				string.Empty
			);

			ServiceMetadataBehavior smb = new ServiceMetadataBehavior();
			switch (binding.Scheme) {
				case "http":
					smb.HttpGetEnabled = true;
					break;
				case "https":
					smb.HttpsGetEnabled = true;
					break;
				case "net.tcp":
					break;
				default:
					ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(binding.Scheme), binding.Scheme));
					goto case "net.tcp";
			}

			ServiceHost.Description.Behaviors.Add(smb);

			try {
				ServiceHost.Open();
			} catch (AddressAccessDeniedException) {
				ASF.ArchiLogger.LogGenericError(Strings.ErrorWCFAddressAccessDeniedException);
				return;
			} catch (Exception e) {
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			ASF.ArchiLogger.LogGenericInfo(Strings.WCFReady);
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

		private static Binding GetTargetBinding() {
			Binding result;
			switch (Program.GlobalConfig.WCFBinding) {
				case GlobalConfig.EWCFBinding.NetTcp:
					result = new NetTcpBinding {
						// This is a balance between default of 8192 which is not enough, and int.MaxValue which is prone to DoS attacks
						// We assume maximum of 255 bots and maximum of 1024 characters per each bot included in the response
						ReaderQuotas = { MaxStringContentLength = byte.MaxValue * 1024 },

						// We use SecurityMode.None for Mono compatibility
						// Yes, also on Windows, for Mono<->Windows communication
						Security = { Mode = SecurityMode.None }
					};

					break;
				case GlobalConfig.EWCFBinding.BasicHttp:
					result = new BasicHttpBinding {
						// This is a balance between default of 8192 which is not enough, and int.MaxValue which is prone to DoS attacks
						// We assume maximum of 255 bots and maximum of 1024 characters per each bot included in the response
						ReaderQuotas = { MaxStringContentLength = byte.MaxValue * 1024 }
					};

					break;
				case GlobalConfig.EWCFBinding.WSHttp:
					result = new WSHttpBinding {
						// This is a balance between default of 8192 which is not enough, and int.MaxValue which is prone to DoS attacks
						// We assume maximum of 255 bots and maximum of 1024 characters per each bot included in the response
						ReaderQuotas = { MaxStringContentLength = byte.MaxValue * 1024 },

						// We use SecurityMode.None for Mono compatibility
						// Yes, also on Windows, for Mono<->Windows communication
						Security = { Mode = SecurityMode.None }
					};

					break;
				default:
					ASF.ArchiLogger.LogGenericWarning(string.Format(Strings.WarningUnknownValuePleaseReport, nameof(Program.GlobalConfig.WCFBinding), Program.GlobalConfig.WCFBinding));
					goto case GlobalConfig.EWCFBinding.NetTcp;
			}

			result.SendTimeout = new TimeSpan(0, 0, Program.GlobalConfig.ConnectionTimeout);
			return result;
		}

		private static string GetUrlFromBinding(Binding binding) {
			if (binding != null) {
				return binding.Scheme + "://" + Program.GlobalConfig.WCFHost + ":" + Program.GlobalConfig.WCFPort + "/ASF";
			}

			ASF.ArchiLogger.LogNullError(nameof(binding));
			return null;
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