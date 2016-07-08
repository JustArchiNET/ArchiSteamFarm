------------------------------------------------------------------------------
v 1.8.0			Jul 8, 2016
------------------------------------------------------------------------------
* Added `CallbackManager.RunWaitAllCallbacks` (pr #292)
* Added `KeyValue.AsUnsignedByte`. (pr #270)
* Added `KeyValue.AsUnsignedInteger`. (pr #255)
* Added `KeyValue.AsUnsignedShort`. (pr #270)
* Added `SteamUserStats.GetNumberOfCurrentPlayers(GameID)`. (pr #234)
* Added the ability to persist the server list to Isolated Storage. (pr #293)
* Added the ability to persist the server list to a file. (pr #293)
* Added support for fetching server list from the Steam Directory API. (pr #293)
* Fixed a crash on Windows if WMI is unavailable.
* Fixed a memory leak when reconnecting to Steam with the same `SteamClient` instance (pr #292)
* Updated `SteamUserStats.GetNumberOfCurrentPlayers` to use messages that Steam continues to respond to. (pr #234)
* Updated Steam enums and protobufs. (pr #271, pr #274, pr #296)
* Updated game-related GC messages and protobufs.
* Removed the hardcoded list of Steam server addresses. (pr #293)

BREAKING CHANGES
* `SmartCMServerList` APIs have changed to accomodate new server management behaviour.


------------------------------------------------------------------------------
v 1.7.0			Dec 21, 2015
------------------------------------------------------------------------------
* Added awaitable API for job-based messages. APIs which returned a `JobID` now return an `AsyncJob<>`, which can be used to asynchronously await for results. (pr #170)
* Added `SteamApps.PICSGetAccessTokens` overload with singular parameters. (pr #190)
* Added `SteamFriends.RequestMessageHistory` and `SteamFriends.RequestOfflineMessages` (pr #193)
* Added the ability to connect to Developer instances of Steam (`EUniverse.Dev`). If anyone at Valve is using this internally, hi!
* Added the ability to set a `LoginID` in `SteamUser.LogOnDetails` so that multiple instances can connect from the same host concurrenctly. (pr #217)
* Added `SteamClient.DebugNetworkListener` API to intercept and log raw messages. (pr #204)
* Added the ability to dump messages in NetHook2 format for debugging purposes. (pr #204)
* Upgraded the encryption protocol used to communicate with the Steam servers.
* Implemented protection against man-in-the-middle attacks. (pr #214)
* Server List will now maintain ordering from Steam, increasing the chances of a successful and geographically local connection. (pr #218)
* After calling `SteamUser.LogOff` or `SteamGameServer.LogOff`, `SteamClient.DisconnectedCallback.UserInitiated` will be `true`. (pr #205)
* Fixed a crash when parsing a Steam ID of the format '[i:1:234]'.
* Fixed a crash when logging on in an environment where the hard disk has no serial ID, such as Hyper-V.
* Fixed a bug when parsing a KeyValue file that contains a `/` followed by a newline. (pr #187)
* Updated Steam enums and protobufs.
* Updated game-related GC messages and protobufs.

BREAKING CHANGES
* SteamKit2 now requires .NET 4.5 or equivalent (Mono 3.0), or higher.
* Removed obsoleted `ICallbackMsg` extension methods `IsType<>` and `Handle<>`. (pr #221)
* Game Coordinator base messages are now generated per-game, instead of relying on Dota 2. GC messages should use the base messages for their game, which is separated by namespace. (pr #180)
* Cell IDs are now consistently `uint`s within `SteamDirectory`.


------------------------------------------------------------------------------
v 1.6.5			Oct 17, 2015
------------------------------------------------------------------------------
* Added inventory service unified protobufs.
* Added the ability to specify the client's prefered Cell ID in `LogOnDetails.CellID`. (pr #148)
* `KeyValue` objects can now be serialized (both text and binary) to streams with `SaveToStream`.
* Fixed an issue with `CDNClient` session initialization involving sessionid values. 
* Added setter for `KeyValue`'s indexer operator.
* Added `ELeaderboardDisplayType` and various leaderboard retrieval functions to `SteamUserStats`. (pr #153)
* Implemented machine id support for logon for when the Steam servers inevitably require it. (pr #152)
* Fixed case where logging on with a different account could lead to an anonymous logon instead. (bug #160)
* `SteamFriends.SetPersonaName` now supports `JobID`s and has a new associated callback: `PersonaChangeCallback`
* Updated game-related GC messages and protobufs.


------------------------------------------------------------------------------
v 1.6.4			Aug 03, 2015
------------------------------------------------------------------------------
* Added smarter server selection logic.
* Added ability to load initial server list from Steam Directory Web API. See `SteamDirectory.Initialize`.
* Added ability to persist internal server list. See Sample 7 for details.
* Added `SteamFriends.InviteUserToChat`.
* Added support in `SteamUser` for passwordless login with a login key.
* Added `NumChatMembers`, `ChatRoomName` and `ChatMembers` to `ChatEnterCallback`.
* Added new API for callback subscriptions, `CallbackManager.Subscribe`.
* Added `SteamApps.RequestFreeLicense` to request Free On-Demand licences.
* Exposed `ClientOSType` and `ClientLanguage` when logging in as a specific or as an anonymous user.
* Fixed `KeyValue` binary deserialization returning a dummy parent node containing the actually deserialized `KeyValue`. You must change to the new `Try`-prefixed methods to adopt the fixed behavior.
* Updated Steam enums and protobufs.
* Updated game-related GC messages and protobufs.

DEPRECATIONS
* `ICallbackMsg.IsType<>` and `ICallbackMsg.Handle<>` are deprecated and will be removed soon in a future version of SteamKit. Please use `CallbackManager.Subscribe` instead.
* `Callback<T>` is deprecated and will be removed in a future version of SteamKit. Please use `CallbackManager.Subscribe` instead.
* `KeyValue.ReadAsBinary` and `KeyValue.LoadAsBinary` are deprecated and will be removed in a future version of SteamKit. Use the `Try`-prefixed methods as outlined above.


------------------------------------------------------------------------------
v 1.6.3			Jun 20, 2015
------------------------------------------------------------------------------

* Added support for parsing older representations of Steam3 Steam IDs such as those from Counter-Strike: Global Offensive, i.e. `[M:1:123(456)]`.
* Steam IDs parsed from Steam3 string representations will now have the correct instance ID set.
* KeyValues can now be serialized to binary, however all values will be serialized as the string type.
* Improved reliability of TCP connections to the CM and UFS servers.
* Added `UserInitiated` property to `SteamClient.DisconnectedCallback` and `UFSClient.DisconnectedCallback` to indicate whether a disconnect was caused by the user, or by another source (Steam servers, bad network connection).
* Updated Steam protobufs.
* Updated game-related GC messages and protobufs.


------------------------------------------------------------------------------
v 1.6.2			Dec 16, 2014
------------------------------------------------------------------------------

*	Fixed a crash when receiving a `ServiceMethod` message.
*	Fixed `ServiceMethodCallback.RpcName` having a leading '.' character (e.g. '.MethodName' instead of 'MethodName).
*	Fixed web responses in `CDNClient` not being closed, which could lead to running out of system resources.
*	Added error handling for `ClientMsgHandler`. Any unhandled exceptions will be logged to `DebugLog` and trigger `SteamClient` to disconnect.
*	Updated `EMsg` list.
*	Updated Steam protobufs.
*	Updated game-related GC messages and protobufs.


------------------------------------------------------------------------------
v 1.6.1			Nov 30, 2014
------------------------------------------------------------------------------

*	Added support for VZip when decompressing depot chunks.
*	Improved thread safety and error handling inside `TcpConnection`.
*	Added `DownloadDepotChunk` overload for consumers who insist on connecting to particular CDNs.
* 	Updated `EResult` with the new field `NotModified`.
*	Updated `EMsg` list.
*	Updated `EOSType`.
	*	The short names for Windows versions (e.g. `Win8` instead of `Windows8`) are preferred.
	*	Addded `MacOS1010` for OS X 10.10 'Yosemite'
*	Removed various long-obsolete values from enums where the value was renamed.
*	Removed `EUniverse.RC`.
*	Updated game related GC messages and protobufs.


------------------------------------------------------------------------------
v 1.6.0			Oct 11, 2014
------------------------------------------------------------------------------

*	Updated EOSType for newer Linux and Windows versions.
*	A LoggedOnCallback with EResult.NoConnection is now posted when attempting to logon without being
	connected to the remote Steam server.
*	Fixed anonymous gameserver logon.
*	CDNClient.Server's constructor now accepts a DnsEndPoint.
*	Updated EResult with the following new fields: AccountLogonDeniedNeedTwoFactorCode, ItemDeleted,
	AccountLoginDeniedThrottle, TwoFactorCodeMismatch
*	Added public utility class for working with DateTime and unix epochs: DateUtils
*	Added GetSingleFileInfo, ShareFile and related callbacks for dealing with Steam cloud files with the
	SteamCloud handler.
*	Fixed a potential crash when failing to properly deserialize network messages.
*	Updated EMsg list.
*	Refactored the internals of tcp connections to Steam servers to be more resiliant and threadsafe.
*	CallbackMsg.Handle will now return a boolean indiciating that the passed in callback matches the
	generic type parameter.
*	Added support for logging into accounts with two-factor auth enabled. See the
	SteamUser.LogOnDetails.TwoFactorCode field.
*	Updated the bootstrap list of Steam CM servers that SteamKit will initially attempt to connect to.
*	Added SteamFriends.FriendMsgEchoCallback for echoed messages sent to other logged in client
	instances.
*	Updated game related GC messages and protobufs.

BREAKING CHANGES
*	JobCallback API has been merged with Callback. For help with transitioning code, please see the following
	wiki notes: https://github.com/SteamRE/SteamKit/wiki/JobCallback-Transition.
*	UFSClient.UploadFileResponseCallback.JobID has been renamed to RemoteJobID in order to not conflict with
	CallbackMsg's new JobID member.
*	UFSClient.UploadDetails.JobID has been renamed to RemoteJobID.
*	CDNClient has been refactored to support multiple authdepot calls for a single instance of the client
	and to support CDN servers.
*	The following EResult fields have been renamed:
		PSNAccountNotLinked -> ExternalAccountUnlinked
		InvalidPSNTicket -> PSNTicketInvalid
		PSNAccountAlreadyLinked -> ExternalAccountAlreadyLinked


------------------------------------------------------------------------------
v 1.5.1			Mar 15, 2014
------------------------------------------------------------------------------

*	Added a parameterless public constructor to DepotManifest.ChunkData to support serialization.
*	SteamWorkshop.RequestPublishedFileDetails has been obsoleted and is no longer supported. This functionality will be 
	dropped in a future SteamKit release. See the the PublishedFile WebAPI service for a functional replacement.
*	Added the request and response messages for the PublishedFile service.
*	Fixed an unhandled exception when requesting metadata-only PICS product info.
*	Exposed the following additional fields in the LoggedOnCallback: VanityURL, NumLoginFailuresToMigrate, NumDisconnectsToMigrate.
*	Exposed the HTTP url details for PICS product info, see: PICSProductInfoCallback.PICSProductInfo.HttpUri and UseHttp.
*	Added EEconTradeResponse.InitiatorPasswordResetProbation and InitiatorNewDeviceCooldown.
*	Fixed SteamGameServer.LogOn and LogOnAnonymous sending the wrong message.
*	Added support for token authentication for game server logon.
*	Added the request and response messages for the GameServers service.
*	Added the ability to specify server type for game servers, see: SteamGameServer.SendStatus.
*	Exposed a few more fields on TradeResultCallback: NumDaysSteamGuardRequired, NumDaysNewDeviceCooldown,
	DefaultNumDaysPasswordResetProbation, NumDaysPasswordResetProbation.
*	Fixed being unable to download depot manifests.
*	Added SteamID.SetFromSteam3String.
*	Obsoleted SteamApps.SendGuestPass. This functionality will be dropped in a future SteamKit release.
*	Updated EResult with the following new fields: UnexpectedError, Disabled, InvalidCEGSubmission, RestrictedDevice.
*	Updated EMsg list.
*	Updated game related GC messages.

BREAKING CHANGES
*	Fixed ServiceMethodResponse.RpcName containing a leading '.'.


------------------------------------------------------------------------------
v 1.5.0			Oct 26, 2013
------------------------------------------------------------------------------

*	Added DebugLog.ClearListeners().
*	Added WebAPI.AsyncInterface, a .NET TPL'd version of WebAPI.Interface.
*	Added SteamClient.ServerListCallback.
*	Added SteamUser.WebAPIUserNonceCallback, and a method to request it: SteamUser.RequestWebAPIUserNonce().
*	Added SteamUser.MarketingMessageCallback.
*	Added a new member to CMClient: CellID. This is the Steam server's recommended CellID.
*	Added the ability to specify AccountID in SteamUser.LogOnDetails.
*	Added a helper API to SteamUnifiedMessages for service messages.
*	Fixed issue where CallbackManager was not triggering for JobCallback<T>.
*	Fixed unhandled protobuf-net exception when (de)serializing messages with enums that are out of date.
*	Fixed a bug where all WebAPI.Interface requests would instantly timeout.
*	Fixed Manifest.HashFileName and Manifest.HashContent being swapped.
*	Updated Emsg list.
*	Updated game related GC messages.
*	Updated the following enums: EResult, EChatEntryType, EAccountFlags, EClanPermission, EFriendFlags, EOSType, EServerType,
	EBillingType, EChatMemberStateChange, EDepotFileFlag, EEconTradeResponse.
*	The following members of EChatRoomEnterResponse have been obsoleted: NoRankingDataLobby, NoRankingDataUser, RankOutOfRange.
*	EOSType.Win7 has been obsoleted and renamed to EOSType.Windows7.
*	EEconTradeResponse.InitiatorAlreadyTrading has been obsoleted and renamed to EEconTradeResponse.AlreadyTrading.
*	EEconTradeResponse.Error has been obsoleted and renamed to EEconTradeResponse.AlreadyHasTradeRequest.
*	EEconTradeResponse.Timeout has been obsoleted and renamed to EEconTradeResponse.NoResponse.
*	EChatEntryType.Emote has been obsoleted. Emotes are no longer supported by Steam.
*	SteamFriends.ProfileInfoCallback.RecentPlaytime has been obsoleted. This data is no longer sent by the Steam servers.
*	Updated to latest protobuf-net.

BREAKING CHANGES
*	SteamUser.LoggedOnCallback.Steam2Ticket is now exposed as a byte array, rather than a Steam2Ticket object.
*	The SteamKit2.Blob namespace and all related classes have been removed.
*	Support for Steam2 servers and the various classes within SteamKit have been removed.
*	CDNClient has been heavily refactored to be more developer friendly.
*	All DateTimes in callbacks are now DateTimeKind.Utc.


------------------------------------------------------------------------------
v 1.4.1			Jul 15, 2013
------------------------------------------------------------------------------

*	Added the ability to manipulate UFS (Steam cloud) files with UFSClient.
*	Added SteamScreenshots handler for interacting with user screenshots.
*	Added an optional parameter to SteamID.Render() to render SteamIDs to their Steam3 representations.
*	Added the ability to specify the timeout of WebAPI requests with Interface.Timeout.
*	The RSACrypto and KeyDictionary utility classes are now accessible to consumers.
*	Updated EMsg list.
*	Updated game related GC messages.


------------------------------------------------------------------------------
v 1.4.0			Jun 08, 2013
------------------------------------------------------------------------------

*	KeyValues now correctly writes out strings in UTF8.
*	Fixed an exception that could occur with an invalid string passed to SteamID's constructor.
*	Added SteamFriends.ClanStateCallback.
*	Added EPersonaStateFlag. This value is now exposed in SteamFriends.PersonaStateCallback.
*	Added MsgClientCreateChat and MsgClientCreateChatResponse messages.
*	Added GlobalID base class for globally unique values (such as JobIDs, UGCHandles) in Steam.
*	Updated EMsg list.
*	Updated game related GC messages.
*	Added initial support for the Steam Cloud file system with UFSClient. This feature should be considered unstable and may
	have breaking changes in the future.

BREAKING CHANGES
*	STATIC_CALLBACKS builds of SteamKit have now been completely removed.
*	Message classes for unified messages have moved namespaces from SteamKit2.Steamworks to SteamKit2.Unified.Internal.


------------------------------------------------------------------------------
v 1.3.1			Mar 10, 2013
------------------------------------------------------------------------------

*	Fixed issue where the avatar hash of a clan was always null.
*	Introduced better handling of networking related cryptographic exceptions.
*	Updated EMsg list.
*	Exposed SteamClient.JobCallback<T> for external consumers.
*	STATIC_CALLBACK builds of SteamKit and related code has been obsoleted and will be removed in the next version.
*	Implemented GameID.ToString().
*	Implemented game pass sending and recieving with SteamApps.SendGuestPass(), SteamApps.GuestPassListCallback, and
	SteamApps.SendGuestPassCallback.
*	Implemented requesting Steam community profile info with SteamFriends.RequestProfileInfo(), and SteamFriends.ProfileInfoCallback
*	CMClient now exposes a ConnectionTimeout field to control the timeout when connecting to Steam. The default timeout is 5 seconds.
*	Updated the internal list of CM servers to help alleviate some issues with connecting to dead servers.
*	Implemented SteamClient.CMListCallback to retrieve the current list of CM servers.
*	Implemented initial support for unified messages through the SteamUnifiedMessages handler.

BREAKING CHANGES
*	CMClient.Connect has been refactored significantly. It is no longer possible to use unencrypted connections. The Connect function
	now accepts an IPEndPoint to allow consumers to specify which Steam server they wish to connect to. Along with this,
	CMClient.Servers is now exposed as a collection of IPEndPoints, instead of IPAddresses.
*	SteamApps.PackageInfoCallback now exposes the immediate child KeyValue for the data, to be more consistent with
	SteamApps.AppInfoCallback.


------------------------------------------------------------------------------
v 1.3.0			Jan 16, 2013
------------------------------------------------------------------------------

*	Fixed case where friend and chat messages were incorrectly trimming the last character.
*	Steam2 ServerClient now exposes a IsConnected property.
*	Steam2 ContentServerClient can now optionally not perform a server handshake when opening a storage session.
*	Added various enums: EClanPermission, EMarketingMessageFlags, ENewsUpdateType, ESystemIMType, EChatFlags,
	ERemoteStoragePlatform, EDRMBlobDownloadType, EDRMBlobDownloadErrorDetail, EClientStat, EClientStatAggregateMethod,
	ELeaderboardDataRequest, ELeaderboardSortMethod, ELeaderboardUploadScoreMethod, and EChatPermission.
*	Fixed case where SteamKit was throwing an unhandled exception during Steam3 tcp connection teardown.
*	Added PICS support to the SteamApps handler: PICSGetAccessTokens, PICSGetChangesSince, and PICSGetProductInfo.
*	Added anonymous download support to CDNClient.
*	Updated the following enums: EMsg, EUniverse, EChatEntryType, EPersonaState, EFriendRelationship, EFriendFlags,
	EClientPersonaStateFlag, ELicenseFlags, ELicenseType, EPaymentMethod, EIntroducerRouting, EClanRank, EClanRelationship,
	EAppInfoSection, EContentDownloadSourceType, EOSType, EServerType, ECurrencyCode, EDepotFileFlag, EEconTradeResponse,
	ESystemIMType, ERemoteStoragePlatform, and EResult.
*	Exposed the following properties in SteamUser.LoggedOnCallback: CellIDPingThreshold, UsePICS, WebAPIUserNonce, and 
	IPCountryCode.
*	Fixed case where SteamKit was incorrectly handling certain logoff messages during Steam server unavailability.
*	Fixed potential crash in Steam2 ContentServerClient when opening a storage session.
*	Updated to latest protobuf-net.

BREAKING CHANGES
*	DepotManifest.ChunkData.CRC is now named DepotManifest.ChunkData.Checksum.


------------------------------------------------------------------------------
v 1.2.2			Nov 11, 2012
------------------------------------------------------------------------------

*	Fixed critical issue that occured while serializing protobuf messages.


------------------------------------------------------------------------------
v 1.2.1			Nov 11, 2012
------------------------------------------------------------------------------

*	Added EPersonaState.LookingToTrade and EPersonaState.LookingToPlay.
*	Added SteamFriends.UnbanChatMember.
*	Removed GeneralDSClient.GetAuthServerList as Steam2 auth servers no longer exist.
*	Removed dependency on Classless.Hasher.
*	Updated to latest protobuf-net.


------------------------------------------------------------------------------
v 1.2.0			Nov 04, 2012
------------------------------------------------------------------------------

*	Fixed issue where LoginKeyCallback was being passed incorrect data.
*	Fixed ClientGCMsg PacketMessage constructor.
*	WebAPI list and array parameters are now accepted and flattened to x[n]=y format.
*	Fixed KeyValue issue when multiple duplicate children exist.
*	Updated protobuf definitions for internal message classes to their latest definitions.
*	Updated EMsgs.
*	Fixed critical MsgMulti handling.
*	Added EEconTradeResponse.
*	Added SteamTrading client message handler.
*	Modified Steam3 TCP socket shutdown to play well with Mono.
*	Modified CMClient.Connect method to be properly async.
*	Implemented friend blocking/unblocking with SteamFriends.IgnoreFriend and SteamFriends.IgnoreFriendCallback.
*	Fixed gameserver logon.
*	Local user is now given the persona name [unassigned] before SteamUser.AccountInfoCallback comes in.
*	Updated SteamKit2's bootstrap CM list, this should reduce how often SK2 will connect to an offline/dead server.
*	Steam2 ServerClient's now expose a ConnectionTimeout member.

BREAKING CHANGES
*	Dota GC EMsgs are now longer located in SteamKit2.GC.Dota.EGCMsg, they are now in SteamKit2.Gc.Dota.Internal.EDOTAGCMsg.
*	Base GC EMsgs are now longer located in SyteamKit2.GC.EGCMsgBase, they are now in multiple enums in the SteamKit2.GC.Internal namespace:
	EGCBaseMsg, EGCSystemMsg, EGCSharedMsg, ESOMsg, EGCItemMsg
*	SteamApps.AppInfoCallback now exposes the immediate child KeyValue for every Section, instead of an empty root parent.


------------------------------------------------------------------------------
v 1.1.0			May 14, 2012
------------------------------------------------------------------------------

*	Added SteamWorkshop for enumerating and requesting details of published workshop files.
*	Large overhaul of SteamGameCoordinator to support the sending and receiving of GC messages.
*	Added SteamFriends ChatInviteCallback.
*	Added SteamFriends KickChatMember and BanChatMember.
*	Fixed invalid handling of PackageInfoCallback response.
*	Updated protobuf definitions for internal message classes to their latest definitions.

BREAKING CHANGES
*	Consumers of SteamClient.JobCallback<T> will have to change their handler functions to take a "JobID" parameter instead of a "ulong".
	These are functionally equivalent, and JobIDs can be implicitly casted to and from ulongs.


------------------------------------------------------------------------------
v 1.0.0			Feb 26, 2012
------------------------------------------------------------------------------

*	Initial release.
