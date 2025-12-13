namespace Login.DBus
{
	using System;
	using Tmds.DBus.Protocol;
	using System.Collections.Generic;
	using System.Threading.Tasks;
	record ManagerProperties
	{
		public bool EnableWallMessages { get; set; } = default!;
		public string WallMessage { get; set; } = default!;
		public uint NAutoVTs { get; set; } = default!;
		public string[] KillOnlyUsers { get; set; } = default!;
		public string[] KillExcludeUsers { get; set; } = default!;
		public bool KillUserProcesses { get; set; } = default!;
		public string RebootParameter { get; set; } = default!;
		public bool RebootToFirmwareSetup { get; set; } = default!;
		public ulong RebootToBootLoaderMenu { get; set; } = default!;
		public string RebootToBootLoaderEntry { get; set; } = default!;
		public string[] BootLoaderEntries { get; set; } = default!;
		public bool IdleHint { get; set; } = default!;
		public ulong IdleSinceHint { get; set; } = default!;
		public ulong IdleSinceHintMonotonic { get; set; } = default!;
		public string BlockInhibited { get; set; } = default!;
		public string BlockWeakInhibited { get; set; } = default!;
		public string DelayInhibited { get; set; } = default!;
		public ulong InhibitDelayMaxUSec { get; set; } = default!;
		public ulong UserStopDelayUSec { get; set; } = default!;
		public string[] SleepOperation { get; set; } = default!;
		public string HandlePowerKey { get; set; } = default!;
		public string HandlePowerKeyLongPress { get; set; } = default!;
		public string HandleRebootKey { get; set; } = default!;
		public string HandleRebootKeyLongPress { get; set; } = default!;
		public string HandleSuspendKey { get; set; } = default!;
		public string HandleSuspendKeyLongPress { get; set; } = default!;
		public string HandleHibernateKey { get; set; } = default!;
		public string HandleHibernateKeyLongPress { get; set; } = default!;
		public string HandleLidSwitch { get; set; } = default!;
		public string HandleLidSwitchExternalPower { get; set; } = default!;
		public string HandleLidSwitchDocked { get; set; } = default!;
		public string HandleSecureAttentionKey { get; set; } = default!;
		public ulong HoldoffTimeoutUSec { get; set; } = default!;
		public string IdleAction { get; set; } = default!;
		public ulong IdleActionUSec { get; set; } = default!;
		public bool PreparingForShutdown { get; set; } = default!;
		public Dictionary<string, VariantValue> PreparingForShutdownWithMetadata { get; set; } = default!;
		public bool PreparingForSleep { get; set; } = default!;
		public (string, ulong) ScheduledShutdown { get; set; } = default!;
		public string DesignatedMaintenanceTime { get; set; } = default!;
		public bool Docked { get; set; } = default!;
		public bool LidClosed { get; set; } = default!;
		public bool OnExternalPower { get; set; } = default!;
		public bool RemoveIPC { get; set; } = default!;
		public ulong RuntimeDirectorySize { get; set; } = default!;
		public ulong RuntimeDirectoryInodesMax { get; set; } = default!;
		public ulong InhibitorsMax { get; set; } = default!;
		public ulong NCurrentInhibitors { get; set; } = default!;
		public ulong SessionsMax { get; set; } = default!;
		public ulong NCurrentSessions { get; set; } = default!;
		public ulong StopIdleSessionUSec { get; set; } = default!;
	}
	partial class Manager : LoginObject
	{
		private const string __Interface = "org.freedesktop.login1.Manager";
		public Manager(LoginService service, ObjectPath path) : base(service, path)
		{ }
		public Task<ObjectPath> GetSessionAsync(string sessionId)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_o(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "GetSession");
				writer.WriteString(sessionId);
				return writer.CreateMessage();
			}
		}
		public Task<ObjectPath> GetSessionByPIDAsync(uint pid)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_o(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "u",
					member: "GetSessionByPID");
				writer.WriteUInt32(pid);
				return writer.CreateMessage();
			}
		}
		public Task<ObjectPath> GetUserAsync(uint uid)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_o(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "u",
					member: "GetUser");
				writer.WriteUInt32(uid);
				return writer.CreateMessage();
			}
		}
		public Task<ObjectPath> GetUserByPIDAsync(uint pid)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_o(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "u",
					member: "GetUserByPID");
				writer.WriteUInt32(pid);
				return writer.CreateMessage();
			}
		}
		public Task<ObjectPath> GetSeatAsync(string seatId)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_o(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "GetSeat");
				writer.WriteString(seatId);
				return writer.CreateMessage();
			}
		}
		public Task<(string, uint, string, string, ObjectPath)[]> ListSessionsAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_arsussoz(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "ListSessions");
				return writer.CreateMessage();
			}
		}
		public Task<(string, uint, string, string, uint, string, string, bool, ulong, ObjectPath)[]> ListSessionsExAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_arsussussbtoz(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "ListSessionsEx");
				return writer.CreateMessage();
			}
		}
		public Task<(uint, string, ObjectPath)[]> ListUsersAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_arusoz(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "ListUsers");
				return writer.CreateMessage();
			}
		}
		public Task<(string, ObjectPath)[]> ListSeatsAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_arsoz(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "ListSeats");
				return writer.CreateMessage();
			}
		}
		public Task<(string, string, string, string, uint, uint)[]> ListInhibitorsAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_arssssuuz(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "ListInhibitors");
				return writer.CreateMessage();
			}
		}
		public Task<(string SessionId, ObjectPath ObjectPath, string RuntimePath, System.Runtime.InteropServices.SafeHandle FifoFd, uint Uid, string SeatId, uint Vtnr, bool Existing)> CreateSessionAsync(uint uid, uint pid, string service, string @type, string @class, string desktop, string seatId, uint vtnr, string tty, string display, bool remote, string remoteUser, string remoteHost, (string, VariantValue)[] properties)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_soshusub(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "uusssssussbssa(sv)",
											 member: "CreateSession");
				writer.WriteUInt32(uid);
				writer.WriteUInt32(pid);
				writer.WriteString(service);
				writer.WriteString(@type);
				writer.WriteString(@class);
				writer.WriteString(desktop);
				writer.WriteString(seatId);
				writer.WriteUInt32(vtnr);
				writer.WriteString(tty);
				writer.WriteString(display);
				writer.WriteBool(remote);
				writer.WriteString(remoteUser);
				writer.WriteString(remoteHost);
				WriteType_arsvz(ref writer, properties);
				return writer.CreateMessage();
			}
		}
		public Task<(string SessionId, ObjectPath ObjectPath, string RuntimePath, System.Runtime.InteropServices.SafeHandle FifoFd, uint Uid, string SeatId, uint Vtnr, bool Existing)> CreateSessionWithPIDFDAsync(uint uid, System.Runtime.InteropServices.SafeHandle pidfd, string service, string @type, string @class, string desktop, string seatId, uint vtnr, string tty, string display, bool remote, string remoteUser, string remoteHost, ulong flags, (string, VariantValue)[] properties)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_soshusub(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "uhsssssussbssta(sv)",
											 member: "CreateSessionWithPIDFD");
				writer.WriteUInt32(uid);
				writer.WriteHandle(pidfd);
				writer.WriteString(service);
				writer.WriteString(@type);
				writer.WriteString(@class);
				writer.WriteString(desktop);
				writer.WriteString(seatId);
				writer.WriteUInt32(vtnr);
				writer.WriteString(tty);
				writer.WriteString(display);
				writer.WriteBool(remote);
				writer.WriteString(remoteUser);
				writer.WriteString(remoteHost);
				writer.WriteUInt64(flags);
				WriteType_arsvz(ref writer, properties);
				return writer.CreateMessage();
			}
		}
		public Task ReleaseSessionAsync(string sessionId)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "ReleaseSession");
				writer.WriteString(sessionId);
				return writer.CreateMessage();
			}
		}
		public Task ActivateSessionAsync(string sessionId)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "ActivateSession");
				writer.WriteString(sessionId);
				return writer.CreateMessage();
			}
		}
		public Task ActivateSessionOnSeatAsync(string sessionId, string seatId)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "ss",
					member: "ActivateSessionOnSeat");
				writer.WriteString(sessionId);
				writer.WriteString(seatId);
				return writer.CreateMessage();
			}
		}
		public Task LockSessionAsync(string sessionId)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "LockSession");
				writer.WriteString(sessionId);
				return writer.CreateMessage();
			}
		}
		public Task UnlockSessionAsync(string sessionId)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "UnlockSession");
				writer.WriteString(sessionId);
				return writer.CreateMessage();
			}
		}
		public Task LockSessionsAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "LockSessions");
				return writer.CreateMessage();
			}
		}
		public Task UnlockSessionsAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "UnlockSessions");
				return writer.CreateMessage();
			}
		}
		public Task KillSessionAsync(string sessionId, string whom, int signalNumber)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "ssi",
					member: "KillSession");
				writer.WriteString(sessionId);
				writer.WriteString(whom);
				writer.WriteInt32(signalNumber);
				return writer.CreateMessage();
			}
		}
		public Task KillUserAsync(uint uid, int signalNumber)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "ui",
					member: "KillUser");
				writer.WriteUInt32(uid);
				writer.WriteInt32(signalNumber);
				return writer.CreateMessage();
			}
		}
		public Task TerminateSessionAsync(string sessionId)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "TerminateSession");
				writer.WriteString(sessionId);
				return writer.CreateMessage();
			}
		}
		public Task TerminateUserAsync(uint uid)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "u",
					member: "TerminateUser");
				writer.WriteUInt32(uid);
				return writer.CreateMessage();
			}
		}
		public Task TerminateSeatAsync(string seatId)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "TerminateSeat");
				writer.WriteString(seatId);
				return writer.CreateMessage();
			}
		}
		public Task SetUserLingerAsync(uint uid, bool enable, bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "ubb",
					member: "SetUserLinger");
				writer.WriteUInt32(uid);
				writer.WriteBool(enable);
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task AttachDeviceAsync(string seatId, string sysfsPath, bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "ssb",
					member: "AttachDevice");
				writer.WriteString(seatId);
				writer.WriteString(sysfsPath);
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task FlushDevicesAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "FlushDevices");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task PowerOffAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "PowerOff");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task PowerOffWithFlagsAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "PowerOffWithFlags");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task RebootAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "Reboot");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task RebootWithFlagsAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "RebootWithFlags");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task HaltAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "Halt");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task HaltWithFlagsAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "HaltWithFlags");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task SuspendAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "Suspend");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task SuspendWithFlagsAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "SuspendWithFlags");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task HibernateAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "Hibernate");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task HibernateWithFlagsAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "HibernateWithFlags");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task HybridSleepAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "HybridSleep");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task HybridSleepWithFlagsAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "HybridSleepWithFlags");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task SuspendThenHibernateAsync(bool interactive)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "SuspendThenHibernate");
				writer.WriteBool(interactive);
				return writer.CreateMessage();
			}
		}
		public Task SuspendThenHibernateWithFlagsAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "SuspendThenHibernateWithFlags");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task SleepAsync(ulong flags)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "Sleep");
				writer.WriteUInt64(flags);
				return writer.CreateMessage();
			}
		}
		public Task<string> CanPowerOffAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanPowerOff");
				return writer.CreateMessage();
			}
		}
		public Task<string> CanRebootAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanReboot");
				return writer.CreateMessage();
			}
		}
		public Task<string> CanHaltAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanHalt");
				return writer.CreateMessage();
			}
		}
		public Task<string> CanSuspendAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanSuspend");
				return writer.CreateMessage();
			}
		}
		public Task<string> CanHibernateAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanHibernate");
				return writer.CreateMessage();
			}
		}
		public Task<string> CanHybridSleepAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanHybridSleep");
				return writer.CreateMessage();
			}
		}
		public Task<string> CanSuspendThenHibernateAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanSuspendThenHibernate");
				return writer.CreateMessage();
			}
		}
		public Task<string> CanSleepAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanSleep");
				return writer.CreateMessage();
			}
		}
		public Task ScheduleShutdownAsync(string @type, ulong usec)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "st",
					member: "ScheduleShutdown");
				writer.WriteString(@type);
				writer.WriteUInt64(usec);
				return writer.CreateMessage();
			}
		}
		public Task<bool> CancelScheduledShutdownAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_b(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CancelScheduledShutdown");
				return writer.CreateMessage();
			}
		}
		public Task<System.Runtime.InteropServices.SafeHandle> InhibitAsync(string what, string who, string why, string mode)
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_h(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "ssss",
					member: "Inhibit");
				writer.WriteString(what);
				writer.WriteString(who);
				writer.WriteString(why);
				writer.WriteString(mode);
				return writer.CreateMessage();
			}
		}
		public Task<string> CanRebootParameterAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanRebootParameter");
				return writer.CreateMessage();
			}
		}
		public Task SetRebootParameterAsync(string parameter)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "SetRebootParameter");
				writer.WriteString(parameter);
				return writer.CreateMessage();
			}
		}
		public Task<string> CanRebootToFirmwareSetupAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanRebootToFirmwareSetup");
				return writer.CreateMessage();
			}
		}
		public Task SetRebootToFirmwareSetupAsync(bool enable)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "b",
					member: "SetRebootToFirmwareSetup");
				writer.WriteBool(enable);
				return writer.CreateMessage();
			}
		}
		public Task<string> CanRebootToBootLoaderMenuAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanRebootToBootLoaderMenu");
				return writer.CreateMessage();
			}
		}
		public Task SetRebootToBootLoaderMenuAsync(ulong timeout)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "t",
					member: "SetRebootToBootLoaderMenu");
				writer.WriteUInt64(timeout);
				return writer.CreateMessage();
			}
		}
		public Task<string> CanRebootToBootLoaderEntryAsync()
		{
			return this.Connection.CallMethodAsync(CreateMessage(), (Message m, object? s) => ReadMessage_s(m, (LoginObject)s!), this);
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					member: "CanRebootToBootLoaderEntry");
				return writer.CreateMessage();
			}
		}
		public Task SetRebootToBootLoaderEntryAsync(string bootLoaderEntry)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "s",
					member: "SetRebootToBootLoaderEntry");
				writer.WriteString(bootLoaderEntry);
				return writer.CreateMessage();
			}
		}
		public Task SetWallMessageAsync(string wallMessage, bool enable)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: __Interface,
					signature: "sb",
					member: "SetWallMessage");
				writer.WriteString(wallMessage);
				writer.WriteBool(enable);
				return writer.CreateMessage();
			}
		}
		public ValueTask<IDisposable> WatchSecureAttentionKeyAsync(Action<Exception?, (string SeatId, ObjectPath ObjectPath)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "SecureAttentionKey", (Message m, object? s) => ReadMessage_so(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchSessionNewAsync(Action<Exception?, (string SessionId, ObjectPath ObjectPath)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "SessionNew", (Message m, object? s) => ReadMessage_so(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchSessionRemovedAsync(Action<Exception?, (string SessionId, ObjectPath ObjectPath)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "SessionRemoved", (Message m, object? s) => ReadMessage_so(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchUserNewAsync(Action<Exception?, (uint Uid, ObjectPath ObjectPath)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "UserNew", (Message m, object? s) => ReadMessage_uo(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchUserRemovedAsync(Action<Exception?, (uint Uid, ObjectPath ObjectPath)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "UserRemoved", (Message m, object? s) => ReadMessage_uo(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchSeatNewAsync(Action<Exception?, (string SeatId, ObjectPath ObjectPath)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "SeatNew", (Message m, object? s) => ReadMessage_so(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchSeatRemovedAsync(Action<Exception?, (string SeatId, ObjectPath ObjectPath)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "SeatRemoved", (Message m, object? s) => ReadMessage_so(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchPrepareForShutdownAsync(Action<Exception?, bool> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "PrepareForShutdown", (Message m, object? s) => ReadMessage_b(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchPrepareForShutdownWithMetadataAsync(Action<Exception?, (bool Start, Dictionary<string, VariantValue> Metadata)> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "PrepareForShutdownWithMetadata", (Message m, object? s) => ReadMessage_baesv(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public ValueTask<IDisposable> WatchPrepareForSleepAsync(Action<Exception?, bool> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		=> base.WatchSignalAsync(Service.Destination, __Interface, Path, "PrepareForSleep", (Message m, object? s) => ReadMessage_b(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
		public Task SetEnableWallMessagesAsync(bool value)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: "org.freedesktop.DBus.Properties",
					signature: "ssv",
					member: "Set");
				writer.WriteString(__Interface);
				writer.WriteString("EnableWallMessages");
				writer.WriteSignature("b");
				writer.WriteBool(value);
				return writer.CreateMessage();
			}
		}
		public Task SetWallMessageAsync(string value)
		{
			return this.Connection.CallMethodAsync(CreateMessage());
			MessageBuffer CreateMessage()
			{
				var writer = this.Connection.GetMessageWriter();
				writer.WriteMethodCallHeader(
					destination: Service.Destination,
					path: Path,
					@interface: "org.freedesktop.DBus.Properties",
					signature: "ssv",
					member: "Set");
				writer.WriteString(__Interface);
				writer.WriteString("WallMessage");
				writer.WriteSignature("s");
				writer.WriteString(value);
				return writer.CreateMessage();
			}
		}
		public Task<bool> GetEnableWallMessagesAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "EnableWallMessages"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<string> GetWallMessageAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "WallMessage"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<uint> GetNAutoVTsAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "NAutoVTs"), (Message m, object? s) => ReadMessage_v_u(m, (LoginObject)s!), this);
		public Task<string[]> GetKillOnlyUsersAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "KillOnlyUsers"), (Message m, object? s) => ReadMessage_v_as(m, (LoginObject)s!), this);
		public Task<string[]> GetKillExcludeUsersAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "KillExcludeUsers"), (Message m, object? s) => ReadMessage_v_as(m, (LoginObject)s!), this);
		public Task<bool> GetKillUserProcessesAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "KillUserProcesses"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<string> GetRebootParameterAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "RebootParameter"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<bool> GetRebootToFirmwareSetupAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "RebootToFirmwareSetup"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<ulong> GetRebootToBootLoaderMenuAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "RebootToBootLoaderMenu"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<string> GetRebootToBootLoaderEntryAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "RebootToBootLoaderEntry"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string[]> GetBootLoaderEntriesAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "BootLoaderEntries"), (Message m, object? s) => ReadMessage_v_as(m, (LoginObject)s!), this);
		public Task<bool> GetIdleHintAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "IdleHint"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<ulong> GetIdleSinceHintAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "IdleSinceHint"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetIdleSinceHintMonotonicAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "IdleSinceHintMonotonic"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<string> GetBlockInhibitedAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "BlockInhibited"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetBlockWeakInhibitedAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "BlockWeakInhibited"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetDelayInhibitedAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "DelayInhibited"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<ulong> GetInhibitDelayMaxUSecAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "InhibitDelayMaxUSec"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetUserStopDelayUSecAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "UserStopDelayUSec"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<string[]> GetSleepOperationAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "SleepOperation"), (Message m, object? s) => ReadMessage_v_as(m, (LoginObject)s!), this);
		public Task<string> GetHandlePowerKeyAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandlePowerKey"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandlePowerKeyLongPressAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandlePowerKeyLongPress"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleRebootKeyAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleRebootKey"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleRebootKeyLongPressAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleRebootKeyLongPress"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleSuspendKeyAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleSuspendKey"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleSuspendKeyLongPressAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleSuspendKeyLongPress"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleHibernateKeyAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleHibernateKey"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleHibernateKeyLongPressAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleHibernateKeyLongPress"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleLidSwitchAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleLidSwitch"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleLidSwitchExternalPowerAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleLidSwitchExternalPower"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleLidSwitchDockedAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleLidSwitchDocked"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<string> GetHandleSecureAttentionKeyAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HandleSecureAttentionKey"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<ulong> GetHoldoffTimeoutUSecAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "HoldoffTimeoutUSec"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<string> GetIdleActionAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "IdleAction"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<ulong> GetIdleActionUSecAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "IdleActionUSec"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<bool> GetPreparingForShutdownAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "PreparingForShutdown"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<Dictionary<string, VariantValue>> GetPreparingForShutdownWithMetadataAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "PreparingForShutdownWithMetadata"), (Message m, object? s) => ReadMessage_v_aesv(m, (LoginObject)s!), this);
		public Task<bool> GetPreparingForSleepAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "PreparingForSleep"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<(string, ulong)> GetScheduledShutdownAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "ScheduledShutdown"), (Message m, object? s) => ReadMessage_v_rstz(m, (LoginObject)s!), this);
		public Task<string> GetDesignatedMaintenanceTimeAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "DesignatedMaintenanceTime"), (Message m, object? s) => ReadMessage_v_s(m, (LoginObject)s!), this);
		public Task<bool> GetDockedAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "Docked"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<bool> GetLidClosedAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "LidClosed"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<bool> GetOnExternalPowerAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "OnExternalPower"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<bool> GetRemoveIPCAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "RemoveIPC"), (Message m, object? s) => ReadMessage_v_b(m, (LoginObject)s!), this);
		public Task<ulong> GetRuntimeDirectorySizeAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "RuntimeDirectorySize"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetRuntimeDirectoryInodesMaxAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "RuntimeDirectoryInodesMax"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetInhibitorsMaxAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "InhibitorsMax"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetNCurrentInhibitorsAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "NCurrentInhibitors"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetSessionsMaxAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "SessionsMax"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetNCurrentSessionsAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "NCurrentSessions"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ulong> GetStopIdleSessionUSecAsync()
		=> this.Connection.CallMethodAsync(CreateGetPropertyMessage(__Interface, "StopIdleSessionUSec"), (Message m, object? s) => ReadMessage_v_t(m, (LoginObject)s!), this);
		public Task<ManagerProperties> GetPropertiesAsync()
		{
			return this.Connection.CallMethodAsync(CreateGetAllPropertiesMessage(__Interface), (Message m, object? s) => ReadMessage(m, (LoginObject)s!), this);
			static ManagerProperties ReadMessage(Message message, LoginObject _)
			{
				var reader = message.GetBodyReader();
				return ReadProperties(ref reader);
			}
		}
		public ValueTask<IDisposable> WatchPropertiesChangedAsync(Action<Exception?, PropertyChanges<ManagerProperties>> handler, bool emitOnCapturedContext = true, ObserverFlags flags = ObserverFlags.None)
		{
			return base.WatchPropertiesChangedAsync(__Interface, (Message m, object? s) => ReadMessage(m, (LoginObject)s!), handler, emitOnCapturedContext, flags);
			static PropertyChanges<ManagerProperties> ReadMessage(Message message, LoginObject _)
			{
				var reader = message.GetBodyReader();
				reader.ReadString(); // interface
				List<string> changed = new(), invalidated = new();
				return new PropertyChanges<ManagerProperties>(ReadProperties(ref reader, changed), ReadInvalidated(ref reader), changed.ToArray());
			}
			static string[] ReadInvalidated(ref Reader reader)
			{
				List<string>? invalidated = null;
				ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.String);
				while (reader.HasNext(arrayEnd))
				{
					invalidated ??= new();
					var property = reader.ReadString();
					switch (property)
					{
						case "EnableWallMessages": invalidated.Add("EnableWallMessages"); break;
						case "WallMessage": invalidated.Add("WallMessage"); break;
						case "NAutoVTs": invalidated.Add("NAutoVTs"); break;
						case "KillOnlyUsers": invalidated.Add("KillOnlyUsers"); break;
						case "KillExcludeUsers": invalidated.Add("KillExcludeUsers"); break;
						case "KillUserProcesses": invalidated.Add("KillUserProcesses"); break;
						case "RebootParameter": invalidated.Add("RebootParameter"); break;
						case "RebootToFirmwareSetup": invalidated.Add("RebootToFirmwareSetup"); break;
						case "RebootToBootLoaderMenu": invalidated.Add("RebootToBootLoaderMenu"); break;
						case "RebootToBootLoaderEntry": invalidated.Add("RebootToBootLoaderEntry"); break;
						case "BootLoaderEntries": invalidated.Add("BootLoaderEntries"); break;
						case "IdleHint": invalidated.Add("IdleHint"); break;
						case "IdleSinceHint": invalidated.Add("IdleSinceHint"); break;
						case "IdleSinceHintMonotonic": invalidated.Add("IdleSinceHintMonotonic"); break;
						case "BlockInhibited": invalidated.Add("BlockInhibited"); break;
						case "BlockWeakInhibited": invalidated.Add("BlockWeakInhibited"); break;
						case "DelayInhibited": invalidated.Add("DelayInhibited"); break;
						case "InhibitDelayMaxUSec": invalidated.Add("InhibitDelayMaxUSec"); break;
						case "UserStopDelayUSec": invalidated.Add("UserStopDelayUSec"); break;
						case "SleepOperation": invalidated.Add("SleepOperation"); break;
						case "HandlePowerKey": invalidated.Add("HandlePowerKey"); break;
						case "HandlePowerKeyLongPress": invalidated.Add("HandlePowerKeyLongPress"); break;
						case "HandleRebootKey": invalidated.Add("HandleRebootKey"); break;
						case "HandleRebootKeyLongPress": invalidated.Add("HandleRebootKeyLongPress"); break;
						case "HandleSuspendKey": invalidated.Add("HandleSuspendKey"); break;
						case "HandleSuspendKeyLongPress": invalidated.Add("HandleSuspendKeyLongPress"); break;
						case "HandleHibernateKey": invalidated.Add("HandleHibernateKey"); break;
						case "HandleHibernateKeyLongPress": invalidated.Add("HandleHibernateKeyLongPress"); break;
						case "HandleLidSwitch": invalidated.Add("HandleLidSwitch"); break;
						case "HandleLidSwitchExternalPower": invalidated.Add("HandleLidSwitchExternalPower"); break;
						case "HandleLidSwitchDocked": invalidated.Add("HandleLidSwitchDocked"); break;
						case "HandleSecureAttentionKey": invalidated.Add("HandleSecureAttentionKey"); break;
						case "HoldoffTimeoutUSec": invalidated.Add("HoldoffTimeoutUSec"); break;
						case "IdleAction": invalidated.Add("IdleAction"); break;
						case "IdleActionUSec": invalidated.Add("IdleActionUSec"); break;
						case "PreparingForShutdown": invalidated.Add("PreparingForShutdown"); break;
						case "PreparingForShutdownWithMetadata": invalidated.Add("PreparingForShutdownWithMetadata"); break;
						case "PreparingForSleep": invalidated.Add("PreparingForSleep"); break;
						case "ScheduledShutdown": invalidated.Add("ScheduledShutdown"); break;
						case "DesignatedMaintenanceTime": invalidated.Add("DesignatedMaintenanceTime"); break;
						case "Docked": invalidated.Add("Docked"); break;
						case "LidClosed": invalidated.Add("LidClosed"); break;
						case "OnExternalPower": invalidated.Add("OnExternalPower"); break;
						case "RemoveIPC": invalidated.Add("RemoveIPC"); break;
						case "RuntimeDirectorySize": invalidated.Add("RuntimeDirectorySize"); break;
						case "RuntimeDirectoryInodesMax": invalidated.Add("RuntimeDirectoryInodesMax"); break;
						case "InhibitorsMax": invalidated.Add("InhibitorsMax"); break;
						case "NCurrentInhibitors": invalidated.Add("NCurrentInhibitors"); break;
						case "SessionsMax": invalidated.Add("SessionsMax"); break;
						case "NCurrentSessions": invalidated.Add("NCurrentSessions"); break;
						case "StopIdleSessionUSec": invalidated.Add("StopIdleSessionUSec"); break;
					}
				}
				return invalidated?.ToArray() ?? Array.Empty<string>();
			}
		}
		private static ManagerProperties ReadProperties(ref Reader reader, List<string>? changedList = null)
		{
			var props = new ManagerProperties();
			ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.Struct);
			while (reader.HasNext(arrayEnd))
			{
				var property = reader.ReadString();
				switch (property)
				{
					case "EnableWallMessages":
						reader.ReadSignature("b"u8);
						props.EnableWallMessages = reader.ReadBool();
						changedList?.Add("EnableWallMessages");
						break;
					case "WallMessage":
						reader.ReadSignature("s"u8);
						props.WallMessage = reader.ReadString();
						changedList?.Add("WallMessage");
						break;
					case "NAutoVTs":
						reader.ReadSignature("u"u8);
						props.NAutoVTs = reader.ReadUInt32();
						changedList?.Add("NAutoVTs");
						break;
					case "KillOnlyUsers":
						reader.ReadSignature("as"u8);
						props.KillOnlyUsers = reader.ReadArrayOfString();
						changedList?.Add("KillOnlyUsers");
						break;
					case "KillExcludeUsers":
						reader.ReadSignature("as"u8);
						props.KillExcludeUsers = reader.ReadArrayOfString();
						changedList?.Add("KillExcludeUsers");
						break;
					case "KillUserProcesses":
						reader.ReadSignature("b"u8);
						props.KillUserProcesses = reader.ReadBool();
						changedList?.Add("KillUserProcesses");
						break;
					case "RebootParameter":
						reader.ReadSignature("s"u8);
						props.RebootParameter = reader.ReadString();
						changedList?.Add("RebootParameter");
						break;
					case "RebootToFirmwareSetup":
						reader.ReadSignature("b"u8);
						props.RebootToFirmwareSetup = reader.ReadBool();
						changedList?.Add("RebootToFirmwareSetup");
						break;
					case "RebootToBootLoaderMenu":
						reader.ReadSignature("t"u8);
						props.RebootToBootLoaderMenu = reader.ReadUInt64();
						changedList?.Add("RebootToBootLoaderMenu");
						break;
					case "RebootToBootLoaderEntry":
						reader.ReadSignature("s"u8);
						props.RebootToBootLoaderEntry = reader.ReadString();
						changedList?.Add("RebootToBootLoaderEntry");
						break;
					case "BootLoaderEntries":
						reader.ReadSignature("as"u8);
						props.BootLoaderEntries = reader.ReadArrayOfString();
						changedList?.Add("BootLoaderEntries");
						break;
					case "IdleHint":
						reader.ReadSignature("b"u8);
						props.IdleHint = reader.ReadBool();
						changedList?.Add("IdleHint");
						break;
					case "IdleSinceHint":
						reader.ReadSignature("t"u8);
						props.IdleSinceHint = reader.ReadUInt64();
						changedList?.Add("IdleSinceHint");
						break;
					case "IdleSinceHintMonotonic":
						reader.ReadSignature("t"u8);
						props.IdleSinceHintMonotonic = reader.ReadUInt64();
						changedList?.Add("IdleSinceHintMonotonic");
						break;
					case "BlockInhibited":
						reader.ReadSignature("s"u8);
						props.BlockInhibited = reader.ReadString();
						changedList?.Add("BlockInhibited");
						break;
					case "BlockWeakInhibited":
						reader.ReadSignature("s"u8);
						props.BlockWeakInhibited = reader.ReadString();
						changedList?.Add("BlockWeakInhibited");
						break;
					case "DelayInhibited":
						reader.ReadSignature("s"u8);
						props.DelayInhibited = reader.ReadString();
						changedList?.Add("DelayInhibited");
						break;
					case "InhibitDelayMaxUSec":
						reader.ReadSignature("t"u8);
						props.InhibitDelayMaxUSec = reader.ReadUInt64();
						changedList?.Add("InhibitDelayMaxUSec");
						break;
					case "UserStopDelayUSec":
						reader.ReadSignature("t"u8);
						props.UserStopDelayUSec = reader.ReadUInt64();
						changedList?.Add("UserStopDelayUSec");
						break;
					case "SleepOperation":
						reader.ReadSignature("as"u8);
						props.SleepOperation = reader.ReadArrayOfString();
						changedList?.Add("SleepOperation");
						break;
					case "HandlePowerKey":
						reader.ReadSignature("s"u8);
						props.HandlePowerKey = reader.ReadString();
						changedList?.Add("HandlePowerKey");
						break;
					case "HandlePowerKeyLongPress":
						reader.ReadSignature("s"u8);
						props.HandlePowerKeyLongPress = reader.ReadString();
						changedList?.Add("HandlePowerKeyLongPress");
						break;
					case "HandleRebootKey":
						reader.ReadSignature("s"u8);
						props.HandleRebootKey = reader.ReadString();
						changedList?.Add("HandleRebootKey");
						break;
					case "HandleRebootKeyLongPress":
						reader.ReadSignature("s"u8);
						props.HandleRebootKeyLongPress = reader.ReadString();
						changedList?.Add("HandleRebootKeyLongPress");
						break;
					case "HandleSuspendKey":
						reader.ReadSignature("s"u8);
						props.HandleSuspendKey = reader.ReadString();
						changedList?.Add("HandleSuspendKey");
						break;
					case "HandleSuspendKeyLongPress":
						reader.ReadSignature("s"u8);
						props.HandleSuspendKeyLongPress = reader.ReadString();
						changedList?.Add("HandleSuspendKeyLongPress");
						break;
					case "HandleHibernateKey":
						reader.ReadSignature("s"u8);
						props.HandleHibernateKey = reader.ReadString();
						changedList?.Add("HandleHibernateKey");
						break;
					case "HandleHibernateKeyLongPress":
						reader.ReadSignature("s"u8);
						props.HandleHibernateKeyLongPress = reader.ReadString();
						changedList?.Add("HandleHibernateKeyLongPress");
						break;
					case "HandleLidSwitch":
						reader.ReadSignature("s"u8);
						props.HandleLidSwitch = reader.ReadString();
						changedList?.Add("HandleLidSwitch");
						break;
					case "HandleLidSwitchExternalPower":
						reader.ReadSignature("s"u8);
						props.HandleLidSwitchExternalPower = reader.ReadString();
						changedList?.Add("HandleLidSwitchExternalPower");
						break;
					case "HandleLidSwitchDocked":
						reader.ReadSignature("s"u8);
						props.HandleLidSwitchDocked = reader.ReadString();
						changedList?.Add("HandleLidSwitchDocked");
						break;
					case "HandleSecureAttentionKey":
						reader.ReadSignature("s"u8);
						props.HandleSecureAttentionKey = reader.ReadString();
						changedList?.Add("HandleSecureAttentionKey");
						break;
					case "HoldoffTimeoutUSec":
						reader.ReadSignature("t"u8);
						props.HoldoffTimeoutUSec = reader.ReadUInt64();
						changedList?.Add("HoldoffTimeoutUSec");
						break;
					case "IdleAction":
						reader.ReadSignature("s"u8);
						props.IdleAction = reader.ReadString();
						changedList?.Add("IdleAction");
						break;
					case "IdleActionUSec":
						reader.ReadSignature("t"u8);
						props.IdleActionUSec = reader.ReadUInt64();
						changedList?.Add("IdleActionUSec");
						break;
					case "PreparingForShutdown":
						reader.ReadSignature("b"u8);
						props.PreparingForShutdown = reader.ReadBool();
						changedList?.Add("PreparingForShutdown");
						break;
					case "PreparingForShutdownWithMetadata":
						reader.ReadSignature("a{sv}"u8);
						props.PreparingForShutdownWithMetadata = reader.ReadDictionaryOfStringToVariantValue();
						changedList?.Add("PreparingForShutdownWithMetadata");
						break;
					case "PreparingForSleep":
						reader.ReadSignature("b"u8);
						props.PreparingForSleep = reader.ReadBool();
						changedList?.Add("PreparingForSleep");
						break;
					case "ScheduledShutdown":
						reader.ReadSignature("(st)"u8);
						props.ScheduledShutdown = ReadType_rstz(ref reader);
						changedList?.Add("ScheduledShutdown");
						break;
					case "DesignatedMaintenanceTime":
						reader.ReadSignature("s"u8);
						props.DesignatedMaintenanceTime = reader.ReadString();
						changedList?.Add("DesignatedMaintenanceTime");
						break;
					case "Docked":
						reader.ReadSignature("b"u8);
						props.Docked = reader.ReadBool();
						changedList?.Add("Docked");
						break;
					case "LidClosed":
						reader.ReadSignature("b"u8);
						props.LidClosed = reader.ReadBool();
						changedList?.Add("LidClosed");
						break;
					case "OnExternalPower":
						reader.ReadSignature("b"u8);
						props.OnExternalPower = reader.ReadBool();
						changedList?.Add("OnExternalPower");
						break;
					case "RemoveIPC":
						reader.ReadSignature("b"u8);
						props.RemoveIPC = reader.ReadBool();
						changedList?.Add("RemoveIPC");
						break;
					case "RuntimeDirectorySize":
						reader.ReadSignature("t"u8);
						props.RuntimeDirectorySize = reader.ReadUInt64();
						changedList?.Add("RuntimeDirectorySize");
						break;
					case "RuntimeDirectoryInodesMax":
						reader.ReadSignature("t"u8);
						props.RuntimeDirectoryInodesMax = reader.ReadUInt64();
						changedList?.Add("RuntimeDirectoryInodesMax");
						break;
					case "InhibitorsMax":
						reader.ReadSignature("t"u8);
						props.InhibitorsMax = reader.ReadUInt64();
						changedList?.Add("InhibitorsMax");
						break;
					case "NCurrentInhibitors":
						reader.ReadSignature("t"u8);
						props.NCurrentInhibitors = reader.ReadUInt64();
						changedList?.Add("NCurrentInhibitors");
						break;
					case "SessionsMax":
						reader.ReadSignature("t"u8);
						props.SessionsMax = reader.ReadUInt64();
						changedList?.Add("SessionsMax");
						break;
					case "NCurrentSessions":
						reader.ReadSignature("t"u8);
						props.NCurrentSessions = reader.ReadUInt64();
						changedList?.Add("NCurrentSessions");
						break;
					case "StopIdleSessionUSec":
						reader.ReadSignature("t"u8);
						props.StopIdleSessionUSec = reader.ReadUInt64();
						changedList?.Add("StopIdleSessionUSec");
						break;
					default:
						reader.ReadVariantValue();
						break;
				}
			}
			return props;
		}
	}
	partial class LoginService
	{
		public Tmds.DBus.Protocol.Connection Connection { get; }
		public string Destination { get; }
		public LoginService(Tmds.DBus.Protocol.Connection connection, string destination)
		=> (Connection, Destination) = (connection, destination);
		public Manager CreateManager(ObjectPath path) => new Manager(this, path);
	}
	class LoginObject
	{
		public LoginService Service { get; }
		public ObjectPath Path { get; }
		protected Tmds.DBus.Protocol.Connection Connection => Service.Connection;
		protected LoginObject(LoginService service, ObjectPath path)
		=> (Service, Path) = (service, path);
		protected MessageBuffer CreateGetPropertyMessage(string @interface, string property)
		{
			var writer = this.Connection.GetMessageWriter();
			writer.WriteMethodCallHeader(
				destination: Service.Destination,
				path: Path,
				@interface: "org.freedesktop.DBus.Properties",
				signature: "ss",
				member: "Get");
			writer.WriteString(@interface);
			writer.WriteString(property);
			return writer.CreateMessage();
		}
		protected MessageBuffer CreateGetAllPropertiesMessage(string @interface)
		{
			var writer = this.Connection.GetMessageWriter();
			writer.WriteMethodCallHeader(
				destination: Service.Destination,
				path: Path,
				@interface: "org.freedesktop.DBus.Properties",
				signature: "s",
				member: "GetAll");
			writer.WriteString(@interface);
			return writer.CreateMessage();
		}
		protected ValueTask<IDisposable> WatchPropertiesChangedAsync<TProperties>(string @interface, MessageValueReader<PropertyChanges<TProperties>> reader, Action<Exception?, PropertyChanges<TProperties>> handler, bool emitOnCapturedContext, ObserverFlags flags)
		{
			var rule = new MatchRule
			{
				Type = MessageType.Signal,
				Sender = Service.Destination,
				Path = Path,
				Interface = "org.freedesktop.DBus.Properties",
				Member = "PropertiesChanged",
				Arg0 = @interface
			};
			return this.Connection.AddMatchAsync(rule, reader,
												 (Exception? ex, PropertyChanges<TProperties> changes, object? rs, object? hs) => ((Action<Exception?, PropertyChanges<TProperties>>)hs!).Invoke(ex, changes),
												 this, handler, emitOnCapturedContext, flags);
		}
		public ValueTask<IDisposable> WatchSignalAsync<TArg>(string sender, string @interface, ObjectPath path, string signal, MessageValueReader<TArg> reader, Action<Exception?, TArg> handler, bool emitOnCapturedContext, ObserverFlags flags)
		{
			var rule = new MatchRule
			{
				Type = MessageType.Signal,
				Sender = sender,
				Path = path,
				Member = signal,
				Interface = @interface
			};
			return this.Connection.AddMatchAsync(rule, reader,
												 (Exception? ex, TArg arg, object? rs, object? hs) => ((Action<Exception?, TArg>)hs!).Invoke(ex, arg),
												 this, handler, emitOnCapturedContext, flags);
		}
		public ValueTask<IDisposable> WatchSignalAsync(string sender, string @interface, ObjectPath path, string signal, Action<Exception?> handler, bool emitOnCapturedContext, ObserverFlags flags)
		{
			var rule = new MatchRule
			{
				Type = MessageType.Signal,
				Sender = sender,
				Path = path,
				Member = signal,
				Interface = @interface
			};
			return this.Connection.AddMatchAsync<object>(rule, (Message message, object? state) => null!,
														 (Exception? ex, object v, object? rs, object? hs) => ((Action<Exception?>)hs!).Invoke(ex), this, handler, emitOnCapturedContext, flags);
		}
		protected static ObjectPath ReadMessage_o(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return reader.ReadObjectPath();
		}
		protected static (string, uint, string, string, ObjectPath)[] ReadMessage_arsussoz(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return ReadType_arsussoz(ref reader);
		}
		protected static (string, uint, string, string, uint, string, string, bool, ulong, ObjectPath)[] ReadMessage_arsussussbtoz(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return ReadType_arsussussbtoz(ref reader);
		}
		protected static (uint, string, ObjectPath)[] ReadMessage_arusoz(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return ReadType_arusoz(ref reader);
		}
		protected static (string, ObjectPath)[] ReadMessage_arsoz(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return ReadType_arsoz(ref reader);
		}
		protected static (string, string, string, string, uint, uint)[] ReadMessage_arssssuuz(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return ReadType_arssssuuz(ref reader);
		}
		protected static (string, ObjectPath, string, System.Runtime.InteropServices.SafeHandle, uint, string, uint, bool) ReadMessage_soshusub(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			var arg0 = reader.ReadString();
			var arg1 = reader.ReadObjectPath();
			var arg2 = reader.ReadString();
			var arg3 = reader.ReadHandle<Microsoft.Win32.SafeHandles.SafeFileHandle>();
			var arg4 = reader.ReadUInt32();
			var arg5 = reader.ReadString();
			var arg6 = reader.ReadUInt32();
			var arg7 = reader.ReadBool();
			return (arg0, arg1, arg2, arg3, arg4, arg5, arg6, arg7);
		}
		protected static string ReadMessage_s(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return reader.ReadString();
		}
		protected static bool ReadMessage_b(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return reader.ReadBool();
		}
		protected static System.Runtime.InteropServices.SafeHandle ReadMessage_h(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			return reader.ReadHandle<Microsoft.Win32.SafeHandles.SafeFileHandle>();
		}
		protected static (string, ObjectPath) ReadMessage_so(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			var arg0 = reader.ReadString();
			var arg1 = reader.ReadObjectPath();
			return (arg0, arg1);
		}
		protected static (uint, ObjectPath) ReadMessage_uo(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			var arg0 = reader.ReadUInt32();
			var arg1 = reader.ReadObjectPath();
			return (arg0, arg1);
		}
		protected static (bool, Dictionary<string, VariantValue>) ReadMessage_baesv(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			var arg0 = reader.ReadBool();
			var arg1 = reader.ReadDictionaryOfStringToVariantValue();
			return (arg0, arg1);
		}
		protected static bool ReadMessage_v_b(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			reader.ReadSignature("b"u8);
			return reader.ReadBool();
		}
		protected static string ReadMessage_v_s(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			reader.ReadSignature("s"u8);
			return reader.ReadString();
		}
		protected static uint ReadMessage_v_u(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			reader.ReadSignature("u"u8);
			return reader.ReadUInt32();
		}
		protected static string[] ReadMessage_v_as(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			reader.ReadSignature("as"u8);
			return reader.ReadArrayOfString();
		}
		protected static ulong ReadMessage_v_t(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			reader.ReadSignature("t"u8);
			return reader.ReadUInt64();
		}
		protected static Dictionary<string, VariantValue> ReadMessage_v_aesv(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			reader.ReadSignature("a{sv}"u8);
			return reader.ReadDictionaryOfStringToVariantValue();
		}
		protected static (string, ulong) ReadMessage_v_rstz(Message message, LoginObject _)
		{
			var reader = message.GetBodyReader();
			reader.ReadSignature("(st)"u8);
			return ReadType_rstz(ref reader);
		}
		protected static (string, ulong) ReadType_rstz(ref Reader reader)
		{
			return (reader.ReadString(), reader.ReadUInt64());
		}
		protected static (string, uint, string, string, ObjectPath)[] ReadType_arsussoz(ref Reader reader)
		{
			List<(string, uint, string, string, ObjectPath)> list = new();
			ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.Struct);
			while (reader.HasNext(arrayEnd))
			{
				list.Add(ReadType_rsussoz(ref reader));
			}
			return list.ToArray();
		}
		protected static (string, uint, string, string, ObjectPath) ReadType_rsussoz(ref Reader reader)
		{
			return (reader.ReadString(), reader.ReadUInt32(), reader.ReadString(), reader.ReadString(), reader.ReadObjectPath());
		}
		protected static (string, uint, string, string, uint, string, string, bool, ulong, ObjectPath)[] ReadType_arsussussbtoz(ref Reader reader)
		{
			List<(string, uint, string, string, uint, string, string, bool, ulong, ObjectPath)> list = new();
			ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.Struct);
			while (reader.HasNext(arrayEnd))
			{
				list.Add(ReadType_rsussussbtoz(ref reader));
			}
			return list.ToArray();
		}
		protected static (string, uint, string, string, uint, string, string, bool, ulong, ObjectPath) ReadType_rsussussbtoz(ref Reader reader)
		{
			return (reader.ReadString(), reader.ReadUInt32(), reader.ReadString(), reader.ReadString(), reader.ReadUInt32(), reader.ReadString(), reader.ReadString(), reader.ReadBool(), reader.ReadUInt64(), reader.ReadObjectPath());
		}
		protected static (uint, string, ObjectPath)[] ReadType_arusoz(ref Reader reader)
		{
			List<(uint, string, ObjectPath)> list = new();
			ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.Struct);
			while (reader.HasNext(arrayEnd))
			{
				list.Add(ReadType_rusoz(ref reader));
			}
			return list.ToArray();
		}
		protected static (uint, string, ObjectPath) ReadType_rusoz(ref Reader reader)
		{
			return (reader.ReadUInt32(), reader.ReadString(), reader.ReadObjectPath());
		}
		protected static (string, ObjectPath)[] ReadType_arsoz(ref Reader reader)
		{
			List<(string, ObjectPath)> list = new();
			ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.Struct);
			while (reader.HasNext(arrayEnd))
			{
				list.Add(ReadType_rsoz(ref reader));
			}
			return list.ToArray();
		}
		protected static (string, ObjectPath) ReadType_rsoz(ref Reader reader)
		{
			return (reader.ReadString(), reader.ReadObjectPath());
		}
		protected static (string, string, string, string, uint, uint)[] ReadType_arssssuuz(ref Reader reader)
		{
			List<(string, string, string, string, uint, uint)> list = new();
			ArrayEnd arrayEnd = reader.ReadArrayStart(DBusType.Struct);
			while (reader.HasNext(arrayEnd))
			{
				list.Add(ReadType_rssssuuz(ref reader));
			}
			return list.ToArray();
		}
		protected static (string, string, string, string, uint, uint) ReadType_rssssuuz(ref Reader reader)
		{
			return (reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadString(), reader.ReadUInt32(), reader.ReadUInt32());
		}
		protected static void WriteType_arsvz(ref MessageWriter writer, (string, VariantValue)[] value)
		{
			ArrayStart arrayStart = writer.WriteArrayStart(DBusType.Struct);
			foreach (var item in value)
			{
				WriteType_rsvz(ref writer, item);
			}
			writer.WriteArrayEnd(arrayStart);
		}
		protected static void WriteType_rsvz(ref MessageWriter writer, (string, VariantValue) value)
		{
			writer.WriteStructureStart();
			writer.WriteString(value.Item1);
			writer.WriteVariant(value.Item2);
		}
	}
	class PropertyChanges<TProperties>
	{
		public PropertyChanges(TProperties properties, string[] invalidated, string[] changed)
		=> (Properties, Invalidated, Changed) = (properties, invalidated, changed);
		public TProperties Properties { get; }
		public string[] Invalidated { get; }
		public string[] Changed { get; }
		public bool HasChanged(string property) => Array.IndexOf(Changed, property) != -1;
		public bool IsInvalidated(string property) => Array.IndexOf(Invalidated, property) != -1;
	}
}
