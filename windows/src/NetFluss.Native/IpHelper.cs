// Copyright (C) 2026 Rana GmbH
//
// This file is part of NetFluss.
//
// NetFluss is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// NetFluss is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with NetFluss. If not, see <https://www.gnu.org/licenses/>.

using System.Runtime.InteropServices;

// Fields below are filled in by the interop marshaller; nullable analysis cannot see that.
#nullable disable

namespace NetFluss.Native;

/// <summary>
/// Hand-written interop for the IP Helper interface table.
///
/// <para><b>Why not System.Net.NetworkInformation:</b> the BCL's
/// <c>IPv4InterfaceStatistics</c> surfaces 32-bit octet counters, which wrap every
/// ~34 seconds on a saturated 1 Gbps link and every ~3 seconds on 10 GbE. A bandwidth
/// meter cannot be built on those. <c>GetIfTable2</c> returns the 64-bit
/// <c>InOctets</c>/<c>OutOctets</c> counters, which is the whole reason for this file.</para>
///
/// <para><b>Why not CsWin32:</b> the generated <c>MIB_IF_ROW2</c> exposes the
/// interface flags as a bitfield whose accessor names have moved between versions.
/// The layout below is pinned by <c>MibIfRow2LayoutTests</c>, which fails loudly on CI
/// if it is ever wrong — see the plausibility walk there.</para>
/// </summary>
internal static partial class IpHelper
{
    internal const uint NO_ERROR = 0;

    /// <summary>IF_MAX_STRING_SIZE (256) + 1, in WCHARs.</summary>
    internal const int IF_MAX_STRING_SIZE_PLUS_ONE = 257;

    internal const int IF_MAX_PHYS_ADDRESS_LENGTH = 32;

    /// <summary>
    /// MIB_IF_TABLE2 is <c>{ ULONG NumEntries; MIB_IF_ROW2 Table[ANY_SIZE]; }</c>.
    /// MIB_IF_ROW2 opens with a ULONG64, so it is 8-byte aligned and the first row
    /// starts at offset 8, not 4. Getting this wrong yields a plausible-looking first
    /// row and garbage for every row after it.
    /// </summary>
    internal const int MIB_IF_TABLE2_ROWS_OFFSET = 8;

    [LibraryImport("iphlpapi.dll")]
    internal static partial uint GetIfTable2(out nint table);

    [LibraryImport("iphlpapi.dll")]
    internal static partial void FreeMibTable(nint memory);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MIB_IF_ROW2
    {
        public ulong InterfaceLuid;
        public uint InterfaceIndex;
        public Guid InterfaceGuid;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IF_MAX_STRING_SIZE_PLUS_ONE)]
        public string Alias;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = IF_MAX_STRING_SIZE_PLUS_ONE)]
        public string Description;

        public uint PhysicalAddressLength;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IF_MAX_PHYS_ADDRESS_LENGTH)]
        public byte[] PhysicalAddress;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = IF_MAX_PHYS_ADDRESS_LENGTH)]
        public byte[] PermanentPhysicalAddress;

        public uint Mtu;

        /// <summary>IANA ifType — see <see cref="Core.AdapterClassifier"/> for the values we act on.</summary>
        public uint Type;

        /// <summary>TUNNEL_TYPE_NONE == 0.</summary>
        public uint TunnelType;

        public uint MediaType;
        public uint PhysicalMediumType;
        public uint AccessType;
        public uint DirectionType;

        /// <summary>
        /// Packed bitfield: bit 0 HardwareInterface, 1 FilterInterface, 2 ConnectorPresent,
        /// 3 NotAuthenticated, 4 NotMediaConnected, 5 Paused, 6 LowPower, 7 EndPointInterface.
        /// </summary>
        public byte InterfaceAndOperStatusFlags;

        public uint OperStatus;
        public uint AdminStatus;
        public uint MediaConnectState;
        public Guid NetworkGuid;
        public uint ConnectionType;

        public ulong TransmitLinkSpeed;
        public ulong ReceiveLinkSpeed;

        public ulong InOctets;
        public ulong InUcastPkts;
        public ulong InNUcastPkts;
        public ulong InDiscards;
        public ulong InErrors;
        public ulong InUnknownProtos;
        public ulong InUcastOctets;
        public ulong InMulticastOctets;
        public ulong InBroadcastOctets;

        public ulong OutOctets;
        public ulong OutUcastPkts;
        public ulong OutNUcastPkts;
        public ulong OutDiscards;
        public ulong OutErrors;
        public ulong OutUcastOctets;
        public ulong OutMulticastOctets;
        public ulong OutBroadcastOctets;
        public ulong OutQLen;

        public readonly bool IsHardwareInterface => (InterfaceAndOperStatusFlags & 0x01) != 0;

        /// <summary>
        /// NDIS/WFP filter pseudo-interfaces mirror the traffic of the adapter they attach to.
        /// Counting them doubles every number, so they never contribute to totals.
        /// </summary>
        public readonly bool IsFilterInterface => (InterfaceAndOperStatusFlags & 0x02) != 0;

        public readonly bool IsConnectorPresent => (InterfaceAndOperStatusFlags & 0x04) != 0;
    }

    /// <summary>IF_OPER_STATUS.</summary>
    internal enum IfOperStatus : uint
    {
        Up = 1,
        Down = 2,
        Testing = 3,
        Unknown = 4,
        Dormant = 5,
        NotPresent = 6,
        LowerLayerDown = 7,
    }
}
