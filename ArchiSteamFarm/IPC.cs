using System;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using ArchiSteamFarm.Localization;

namespace ArchiSteamFarm {
	internal static class IPC {
		internal static bool KeepRunning { get; private set; }

		private static readonly HttpListener HttpListener = new HttpListener();

		internal static void Initialize(string host, ushort port) {
			if (string.IsNullOrEmpty(host) || (port == 0)) {
				ASF.ArchiLogger.LogNullError(nameof(host) + " || " + nameof(port));
				return;
			}

			if (HttpListener.Prefixes.Count > 0) {
				return;
			}

			string url = "http://" + host + ":" + port + "/" + nameof(IPC) + "/";

			ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCStarting, url));

			HttpListener.Prefixes.Add(url);
		}

		internal static void Start() {
			if (KeepRunning) {
				return;
			}

			try {
				HttpListener.Start();
			} catch (HttpListenerException e) {
				ASF.ArchiLogger.LogGenericException(e);
				return;
			}

			KeepRunning = true;
			Task.Factory.StartNew(Run, TaskCreationOptions.DenyChildAttach | TaskCreationOptions.LongRunning).Forget();

			ASF.ArchiLogger.LogGenericInfo(Strings.IPCReady);
		}

		internal static void Stop() {
			if (!KeepRunning) {
				return;
			}

			KeepRunning = false;
			HttpListener.Stop();
		}

		private static async Task HandleRequest(HttpListenerContext context) {
			if (context == null) {
				ASF.ArchiLogger.LogNullError(nameof(context));
				return;
			}

			if (Program.GlobalConfig.SteamOwnerID == 0) {
				ASF.ArchiLogger.LogGenericInfo(Strings.ErrorIPCAccessDenied);
				await context.Response.WriteAsync(HttpStatusCode.Forbidden, Strings.ErrorIPCAccessDenied).ConfigureAwait(false);
				return;
			}

			if (!context.Request.RawUrl.StartsWith("/" + nameof(IPC), StringComparison.Ordinal)) {
				await context.Response.WriteAsync(HttpStatusCode.BadRequest, nameof(HttpStatusCode.BadRequest)).ConfigureAwait(false);
				return;
			}

			switch (context.Request.HttpMethod) {
				case WebRequestMethods.Http.Get:
					for (int i = 0; i < context.Request.QueryString.Count; i++) {
						string key = context.Request.QueryString.GetKey(i);

						switch (key) {
							case "command":
								string command = context.Request.QueryString.Get(i);
								if (string.IsNullOrEmpty(command)) {
									break;
								}

								Bot bot = Bot.Bots.Values.FirstOrDefault();
								if (bot == null) {
									await context.Response.WriteAsync(HttpStatusCode.NotAcceptable, Strings.ErrorNoBotsDefined).ConfigureAwait(false);
									return;
								}

								if (command[0] != '!') {
									command = "!" + command;
								}

								string response = await bot.Response(Program.GlobalConfig.SteamOwnerID, command).ConfigureAwait(false);

								ASF.ArchiLogger.LogGenericInfo(string.Format(Strings.IPCAnswered, command, response));

								await context.Response.WriteAsync(HttpStatusCode.OK, response).ConfigureAwait(false);
								break;
						}
					}

					break;
				default:
					await context.Response.WriteAsync(HttpStatusCode.BadRequest, nameof(HttpStatusCode.BadRequest)).ConfigureAwait(false);
					return;
			}

			if (context.Response.ContentLength64 == 0) {
				await context.Response.WriteAsync(HttpStatusCode.MethodNotAllowed, nameof(HttpStatusCode.MethodNotAllowed)).ConfigureAwait(false);
			}
		}

		private static async Task Run() {
			while (KeepRunning && HttpListener.IsListening) {
				HttpListenerContext context;

				try {
					context = await HttpListener.GetContextAsync().ConfigureAwait(false);
				} catch (HttpListenerException e) {
					ASF.ArchiLogger.LogGenericException(e);
					continue;
				}

				await HandleRequest(context).ConfigureAwait(false);
				context.Response.Close();
			}
		}

		private static async Task WriteAsync(this HttpListenerResponse response, HttpStatusCode statusCode, string message) {
			if (string.IsNullOrEmpty(message)) {
				ASF.ArchiLogger.LogNullError(nameof(message));
				return;
			}

			try {
				if (response.StatusCode != (ushort) statusCode) {
					response.StatusCode = (ushort) statusCode;
				}

				byte[] buffer = Encoding.UTF8.GetBytes(message);
				response.ContentLength64 = buffer.Length;
				await response.OutputStream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
			} catch (Exception e) {
				response.StatusCode = (ushort) HttpStatusCode.InternalServerError;
				ASF.ArchiLogger.LogGenericDebugException(e);
			}
		}
	}
}