using System;
using System.ComponentModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Windows.Devices.Display;
using Windows.Devices.Display.Core;

namespace DisplayPathTest;

internal static class DisplayHelper
{
	public static void CheckDisplays()
	{
		using DisplayManager manager = DisplayManager.Create(DisplayManagerOptions.None);
		DisplayState state = manager.TryReadCurrentStateForAllTargets().State;
		foreach (var view in state.Views)
		{
			foreach (var path in view.Paths)
			{
				DisplayMonitor? monitor = path.Target.TryGetMonitor();
				if (monitor is not null)
				{
					Console.WriteLine($"[{monitor.DisplayName}]");
					Console.WriteLine($"  Device ID           : {monitor.DeviceId}");

					uint sourceId;
					if ((object)path is IDisplayPathInterop ip)
					{
						ip.GetSourceId(out sourceId);
						Console.WriteLine("  IDisplayPathInterop : OK");
					}
					else // handle older Windows versions
					{
						sourceId = GetSourceId(path.Target.AdapterRelativeId);
						Console.WriteLine("  IDisplayPathInterop : NO");
					}

					string gdiDeviceName = GetGdiDeviceName(monitor.DisplayAdapterId.HighPart, monitor.DisplayAdapterId.LowPart, sourceId);
					Console.WriteLine($"  GDI Name            : {gdiDeviceName}");

					// use gdiDeviceName to correlate with other APIs such as Screen from WinForms
					Screen? screen = Screen.AllScreens.FirstOrDefault(s => s.DeviceName == gdiDeviceName);
					if (screen is not null)
					{
						Console.WriteLine($"  Is Primary          : {screen.Primary}");
						Console.WriteLine($"  Working Area        : {screen.WorkingArea}");
					}

					var presentationRate = path.PresentationRate;
					if (presentationRate is not null)
					{
						var rate = presentationRate.Value.VerticalSyncRate;
						Console.WriteLine($"  Refresh Rate        : {rate.Numerator / (float)rate.Denominator}");
					}

					var modes = path.FindModes(DisplayModeQueryOptions.None);
					Console.WriteLine($"  Modes:              : {modes.Count}");

					Console.WriteLine();
				}
			}
		}
	}

	private static string GetGdiDeviceName(int adapterIdHigh, uint adapterIdLow, uint sourceId)
	{
		var info = new DISPLAYCONFIG_SOURCE_DEVICE_NAME();
		info.header.type = DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME;
		info.header.size = Marshal.SizeOf<DISPLAYCONFIG_SOURCE_DEVICE_NAME>();
		info.header.adapterIdHigh = adapterIdHigh;
		info.header.adapterIdLow = adapterIdLow;
		info.header.id = sourceId;
		int error = DisplayConfigGetDeviceInfo(ref info);
		if (error != ERROR_SUCCESS)
			throw new Win32Exception(error);

		return info.viewGdiDeviceName;
	}

	private static uint GetSourceId(uint targetId)
	{
		int error = GetDisplayConfigBufferSizes(QDC.QDC_ONLY_ACTIVE_PATHS, out int pathCount, out int modeCount);
		if (error != ERROR_SUCCESS)
			throw new Win32Exception(error);

		var paths = new DISPLAYCONFIG_PATH_INFO[pathCount];
		var modes = new DISPLAYCONFIG_MODE_INFO[modeCount];
		error = QueryDisplayConfig(QDC.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero);
		if (error != ERROR_SUCCESS)
			throw new Win32Exception(error);

		return paths.First(p => p.targetInfo.id == targetId).sourceInfo.id;
	}

	#region Win32

	[DllImport("User32")]
	private static extern int DisplayConfigGetDeviceInfo(ref DISPLAYCONFIG_SOURCE_DEVICE_NAME requestPacket);

	[DllImport("User32")]
	private static extern int GetDisplayConfigBufferSizes(
		QDC flags,
		out int numPathArrayElements,
		out int numModeInfoArrayElements);

	[DllImport("User32")]
	private static extern int QueryDisplayConfig(
		QDC flags,
		ref int numPathArrayElements,
		[In, Out] DISPLAYCONFIG_PATH_INFO[] pathArray,
		ref int numModeInfoArrayElements,
		[In, Out] DISPLAYCONFIG_MODE_INFO[] modeInfoArray,
		IntPtr currentTopologyId);

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_DESKTOP_IMAGE_INFO
	{
		public POINTL PathSourceSize;
		public RECT DesktopImageRegion;
		public RECT DesktopImageClip;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_DEVICE_INFO_HEADER
	{
		public int type;
		public int size;
		public uint adapterIdLow;
		public int adapterIdHigh;
		public uint id;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_MODE_INFO
	{
		public DISPLAYCONFIG_MODE_INFO_TYPE infoType;
		public uint id;
		public LUID adapterId;
		public DISPLAYCONFIG_MODE_INFO_union info;
	}

	private enum DISPLAYCONFIG_MODE_INFO_TYPE
	{
		DISPLAYCONFIG_MODE_INFO_TYPE_SOURCE = 1,
		DISPLAYCONFIG_MODE_INFO_TYPE_TARGET = 2,
		DISPLAYCONFIG_MODE_INFO_TYPE_DESKTOP_IMAGE = 3,
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct DISPLAYCONFIG_MODE_INFO_union
	{
		[FieldOffset(0)]
		public DISPLAYCONFIG_TARGET_MODE targetMode;

		[FieldOffset(0)]
		public DISPLAYCONFIG_SOURCE_MODE sourceMode;

		[FieldOffset(0)]
		public DISPLAYCONFIG_DESKTOP_IMAGE_INFO desktopImageInfo;
	}

	private enum DISPLAYCONFIG_PATH
	{
		DISPLAYCONFIG_PATH_ACTIVE = 0x00000001,
		DISPLAYCONFIG_PATH_PREFERRED_UNSCALED = 0x00000004,
		DISPLAYCONFIG_PATH_SUPPORT_VIRTUAL_MODE = 0x00000008,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_PATH_INFO
	{
		public DISPLAYCONFIG_PATH_SOURCE_INFO sourceInfo;
		public DISPLAYCONFIG_PATH_TARGET_INFO targetInfo;
		public DISPLAYCONFIG_PATH flags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_PATH_SOURCE_INFO
	{
		public LUID adapterId;
		public uint id;
		public uint modeInfoIdx;
		public DISPLAYCONFIG_SOURCE_FLAGS statusFlags;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_PATH_TARGET_INFO
	{
		public LUID adapterId;
		public uint id;
		public uint modeInfoIdx;
		public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
		public DISPLAYCONFIG_ROTATION rotation;
		public DISPLAYCONFIG_SCALING scaling;
		public DISPLAYCONFIG_RATIONAL refreshRate;
		public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
		public bool targetAvailable;
		public DISPLAYCONFIG_TARGET_FLAGS statusFlags;
	}

	private enum DISPLAYCONFIG_PIXELFORMAT
	{
		DISPLAYCONFIG_PIXELFORMAT_8BPP = 1,
		DISPLAYCONFIG_PIXELFORMAT_16BPP = 2,
		DISPLAYCONFIG_PIXELFORMAT_24BPP = 3,
		DISPLAYCONFIG_PIXELFORMAT_32BPP = 4,
		DISPLAYCONFIG_PIXELFORMAT_NONGDI = 5,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_RATIONAL
	{
		public uint Numerator;
		public uint Denominator;
	}

	private enum DISPLAYCONFIG_ROTATION
	{
		DISPLAYCONFIG_ROTATION_IDENTITY = 1,
		DISPLAYCONFIG_ROTATION_ROTATE90 = 2,
		DISPLAYCONFIG_ROTATION_ROTATE180 = 3,
	}

	private enum DISPLAYCONFIG_SCANLINE_ORDERING
	{
		DISPLAYCONFIG_SCANLINE_ORDERING_UNSPECIFIED = 0,
		DISPLAYCONFIG_SCANLINE_ORDERING_PROGRESSIVE = 1,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED = 2,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_UPPERFIELDFIRST = DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED,
		DISPLAYCONFIG_SCANLINE_ORDERING_INTERLACED_LOWERFIELDFIRST = 3,
	}

	private enum DISPLAYCONFIG_SCALING
	{
		DISPLAYCONFIG_SCALING_IDENTITY = 1,
		DISPLAYCONFIG_SCALING_CENTERED = 2,
		DISPLAYCONFIG_SCALING_STRETCHED = 3,
		DISPLAYCONFIG_SCALING_ASPECTRATIOCENTEREDMAX = 4,
		DISPLAYCONFIG_SCALING_CUSTOM = 5,
		DISPLAYCONFIG_SCALING_PREFERRED = 128,
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAYCONFIG_SOURCE_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
		public string viewGdiDeviceName;
	}

	private enum DISPLAYCONFIG_SOURCE_FLAGS
	{
		DISPLAYCONFIG_SOURCE_IN_USE = 0x00000001,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_SOURCE_MODE
	{
		public uint width;
		public uint height;
		public DISPLAYCONFIG_PIXELFORMAT pixelFormat;
		public POINTL position;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAYCONFIG_TARGET_DEVICE_NAME
	{
		public DISPLAYCONFIG_DEVICE_INFO_HEADER header;
		public DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS flags;
		public DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY outputTechnology;
		public ushort edidManufactureId;
		public ushort edidProductCodeId;
		public uint connectorInstance;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
		public string monitorFriendlyDeviceName;

		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string monitorDevicePat;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DISPLAYCONFIG_TARGET_DEVICE_NAME_FLAGS
	{
		public uint value;
	}

	private enum DISPLAYCONFIG_TARGET_FLAGS
	{
		DISPLAYCONFIG_TARGET_IN_USE = 0x00000001,
		DISPLAYCONFIG_TARGET_FORCIBLE = 0x00000002,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_BOOT = 0x00000004,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_PATH = 0x00000008,
		DISPLAYCONFIG_TARGET_FORCED_AVAILABILITY_SYSTEM = 0x00000010,
		DISPLAYCONFIG_TARGET_IS_HMD = 0x00000020,
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_TARGET_MODE
	{
		public DISPLAYCONFIG_VIDEO_SIGNAL_INFO targetVideoSignalInfo;
	}

	private enum DISPLAYCONFIG_VIDEO_OUTPUT_TECHNOLOGY
	{
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_OTHER = -1,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HD15 = 0,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SVIDEO = 1,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPOSITE_VIDEO = 2,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_COMPONENT_VIDEO = 3,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DVI = 4,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_HDMI = 5,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_LVDS = 6,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_D_JPN = 8,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDI = 9,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EXTERNAL = 10,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_DISPLAYPORT_EMBEDDED = 11,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EXTERNAL = 12,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_UDI_EMBEDDED = 13,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_SDTVDONGLE = 14,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_MIRACAST = 15,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_WIRED = 16,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INDIRECT_VIRTUAL = 17,
		DISPLAYCONFIG_OUTPUT_TECHNOLOGY_INTERNAL = unchecked((int)0x80000000),
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_VIDEO_SIGNAL_INFO
	{
		public ulong pixelRate;
		public DISPLAYCONFIG_RATIONAL hSyncFreq;
		public DISPLAYCONFIG_RATIONAL vSyncFreq;
		public DISPLAYCONFIG_2DREGION activeSize;
		public DISPLAYCONFIG_2DREGION totalSize;
		public uint videoStandard;
		public DISPLAYCONFIG_SCANLINE_ORDERING scanLineOrdering;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct DISPLAYCONFIG_2DREGION
	{
		public uint cx;
		public uint cy;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct LUID
	{
		public uint LowPart;
		public int HighPart;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct POINTL
	{
		public int x;
		public int y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int left;
		public int top;
		public int right;
		public int bottom;
	}

	private enum QDC
	{
		QDC_ALL_PATHS = 0x00000001,
		QDC_ONLY_ACTIVE_PATHS = 0x00000002,
		QDC_DATABASE_CURRENT = 0x00000004,
		QDC_VIRTUAL_MODE_AWARE = 0x00000010,
		QDC_INCLUDE_HMD = 0x00000020,
	}

	private const int DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME = 1;

	private const int ERROR_SUCCESS = 0;

	#endregion

	#region COM

	// https://learn.microsoft.com/en-us/windows/win32/api/windows.devices.display.core.interop/nn-windows-devices-display-core-interop-idisplaypathinterop
	[ComImport, Guid("A6BA4205-E59E-4E71-B25B-4E436D21EE3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
	public interface IDisplayPathInterop
	{
		[PreserveSig]
		int CreateSourcePresentationHandle(out IntPtr value);

		[PreserveSig]
		int GetSourceId(out uint sourceId);
	}

	#endregion
}
