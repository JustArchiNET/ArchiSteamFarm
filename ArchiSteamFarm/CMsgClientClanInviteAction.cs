using SteamKit2;
using SteamKit2.Internal;
using System.IO;

namespace ArchiSteamFarm {
	/// <summary>
	/// Message used to Accept or Decline a group(clan) invite.
	/// </summary>
	internal sealed class CMsgClientClanInviteAction : ISteamSerializableMessage, ISteamSerializable {
		EMsg ISteamSerializableMessage.GetEMsg() {
			return EMsg.ClientAcknowledgeClanInvite;
		}

		public CMsgClientClanInviteAction() {
		}

		/// <summary>
		/// Group invited to.
		/// </summary>
		internal ulong GroupID = 0;

		/// <summary>
		/// To accept or decline the invite.
		/// </summary>
		internal bool AcceptInvite = true;

		void ISteamSerializable.Serialize(Stream stream) {
			try {
				BinaryWriter binaryWriter = new BinaryWriter(stream);
				binaryWriter.Write(GroupID);
				binaryWriter.Write(AcceptInvite);
			} catch {
				throw new IOException();
			}
		}

		void ISteamSerializable.Deserialize(Stream stream) {
			try {
				BinaryReader binaryReader = new BinaryReader(stream);
				GroupID = binaryReader.ReadUInt64();
				AcceptInvite = binaryReader.ReadBoolean();
			} catch {
				throw new IOException();
			}
		}
	}
}
